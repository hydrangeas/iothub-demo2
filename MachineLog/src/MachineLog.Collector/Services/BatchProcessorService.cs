using MachineLog.Collector.Models;
using MachineLog.Common.Models;
using MachineLog.Common.Synchronization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace MachineLog.Collector.Services;

/// <summary>
/// バッチ処理サービスの実装
/// </summary>
public class BatchProcessorService : IBatchProcessorService, IDisposable, IAsyncDisposable
{
  private readonly ILogger<BatchProcessorService> _logger;
  private readonly BatchConfig _config;
  private readonly IIoTHubService _iotHubService;
  private readonly ConcurrentQueue<LogEntry> _batchQueue;
  private readonly CancellationTokenSource _processingTokenSource;
  private Task? _processingTask;
  private long _currentBatchSizeBytes;
  private bool _isProcessing;
  private readonly AsyncLock _processingLock;
  private readonly SemaphoreSlim _batchSizeLock = new SemaphoreSlim(1, 1);
  private readonly ReaderWriterLockSlim _queueLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
  private bool _isDisposed;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="config">バッチ設定</param>
  /// <param name="iotHubService">IoT Hubサービス</param>
  public BatchProcessorService(
      ILogger<BatchProcessorService> logger,
      IOptions<BatchConfig> config,
      IIoTHubService iotHubService)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    _iotHubService = iotHubService ?? throw new ArgumentNullException(nameof(iotHubService));
    _batchQueue = new ConcurrentQueue<LogEntry>();
    _processingTokenSource = new CancellationTokenSource();
    _currentBatchSizeBytes = 0;
    _isProcessing = false;
    _processingLock = new AsyncLock();

    // デッドロック検出を有効化
    DeadlockDetector.Enable();
  }

  /// <summary>
  /// エントリをバッチに追加します
  /// </summary>
  public virtual async Task<bool> AddToBatchAsync(LogEntry entry, CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorService));

    if (entry == null)
    {
      _logger.LogWarning("Null entry was provided to batch processor");
      return false;
    }

    // バッチが満杯の場合は処理を実行
    if (IsBatchFull())
    {
      _logger.LogInformation("Batch is full, processing before adding new entry");
      await ProcessBatchAsync(false, cancellationToken);
    }

    // エントリをキューに追加（スレッドセーフな操作）
    _queueLock.EnterWriteLock();
    try
    {
      _batchQueue.Enqueue(entry);
    }
    finally
    {
      _queueLock.ExitWriteLock();
    }

    // バッチサイズを更新（アトミック操作）
    await _batchSizeLock.WaitAsync(cancellationToken);
    try
    {
      _currentBatchSizeBytes += EstimateEntrySize(entry);
    }
    finally
    {
      _batchSizeLock.Release();
    }

    _logger.LogDebug("Added entry to batch. Current count: {Count}, Size: {Size} bytes",
        _batchQueue.Count, _currentBatchSizeBytes);

    return true;
  }

  /// <summary>
  /// 複数のエントリをバッチに追加します
  /// </summary>
  public virtual async Task<bool> AddRangeToBatchAsync(IEnumerable<LogEntry> entries, CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorService));

    if (entries == null)
    {
      _logger.LogWarning("Null entries collection was provided to batch processor");
      return false;
    }

    var entriesList = entries.ToList();
    if (entriesList.Count == 0)
    {
      return true;
    }

    // バッチが満杯の場合は処理を実行
    if (IsBatchFull())
    {
      _logger.LogInformation("Batch is full, processing before adding new entries");
      await ProcessBatchAsync(false, cancellationToken);
    }

    // エントリをキューに追加（スレッドセーフな操作）
    _queueLock.EnterWriteLock();
    try
    {
      foreach (var entry in entriesList)
      {
        if (entry != null)
        {
          _batchQueue.Enqueue(entry);
        }
      }
    }
    finally
    {
      _queueLock.ExitWriteLock();
    }

    // バッチサイズを更新（アトミック操作）
    await _batchSizeLock.WaitAsync(cancellationToken);
    try
    {
      foreach (var entry in entriesList)
      {
        if (entry != null)
        {
          _currentBatchSizeBytes += EstimateEntrySize(entry);
        }
      }
    }
    finally
    {
      _batchSizeLock.Release();
    }

    _logger.LogDebug("Added {Count} entries to batch. Current count: {TotalCount}, Size: {Size} bytes",
        entriesList.Count, _batchQueue.Count, _currentBatchSizeBytes);

    return true;
  }

  /// <summary>
  /// 現在のバッチを処理します
  /// </summary>
  public virtual async Task<BatchProcessingResult> ProcessBatchAsync(bool force = false, CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorService));

    var result = new BatchProcessingResult
    {
      Success = true,
      ProcessedItems = 0,
      BatchSizeBytes = _currentBatchSizeBytes
    };

    // デッドロック検出
    if (DeadlockDetector.TryAcquireLock("BatchProcessing", 5000))
    {
      _logger.LogWarning("潜在的なデッドロックを検出しました。処理を続行します。");
    }

    // 非同期ロックを使用して同時実行を防止
    using (await _processingLock.LockAsync(cancellationToken))
    {
      var stopwatch = Stopwatch.StartNew();

      try
      {
        // バッチが空で、強制処理でない場合は何もしない
        if (GetBatchItemCount() == 0 && !force)
        {
          _logger.LogDebug("Batch is empty, nothing to process");
          return result;
        }

        _logger.LogInformation("Processing batch with {Count} items, {Size} bytes",
            GetBatchItemCount(), GetBatchSizeBytes());

        // バッチ内のアイテムを取得
        List<LogEntry> itemsToProcess = new List<LogEntry>();
        
        _queueLock.EnterWriteLock();
        try
        {
          // キューからすべてのアイテムを取得
          while (_batchQueue.TryDequeue(out var item))
          {
            if (item != null)
            {
              itemsToProcess.Add(item);
            }
          }
          
          // バッチサイズをリセット
          Interlocked.Exchange(ref _currentBatchSizeBytes, 0);
        }
        finally
        {
          _queueLock.ExitWriteLock();
        }

        // 処理するアイテムがある場合
        if (itemsToProcess.Count > 0)
        {
          result.ProcessedItems = itemsToProcess.Count;
          
          // 並列処理の最適化（最大同時実行数を設定）
          int maxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4);
          
          // アイテムを並列処理
          await SynchronizationUtility.ParallelForEachAsync(
              itemsToProcess,
              async (item, ct) =>
              {
                // ここでアイテムごとの処理を実装
                // 例: IoT Hubにアップロードなど
                await Task.Delay(1, ct); // 実際の処理に置き換える
                return true;
              },
              maxDegreeOfParallelism,
              cancellationToken);
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error processing batch");
        result.Success = false;
        result.ErrorMessage = ex.Message;
        result.Exception = ex;
      }
      finally
      {
        stopwatch.Stop();
        result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
        
        // デッドロック検出のロックを解放
        DeadlockDetector.ReleaseLock("BatchProcessing");
      }
    }

    return result;
  }

  /// <summary>
  /// バッチ処理を開始します
  /// </summary>
  public virtual async Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorService));

    if (_isProcessing)
    {
      _logger.LogWarning("Batch processor is already running");
      return;
    }

    _logger.LogInformation("Starting batch processor");
    _isProcessing = true;

    // 非同期メソッドの待機を確実にするため、TaskCompletionSourceを使用
    var tcs = new TaskCompletionSource<bool>();

    // 定期的なバッチ処理タスクを開始
    _processingTask = Task.Run(async () =>
    {
      try
      {
        // 開始をシグナル
        tcs.TrySetResult(true);

        while (!_processingTokenSource.Token.IsCancellationRequested)
        {
          try
          {
            await ProcessBatchAsync(true, _processingTokenSource.Token);
          }
          catch (OperationCanceledException) when (_processingTokenSource.Token.IsCancellationRequested)
          {
            // キャンセルは正常な動作
            break;
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "Error in batch processing loop");
            // エラーが発生しても処理を継続
          }

          // 次の処理までの待機
          await Task.Delay(TimeSpan.FromSeconds(_config.ProcessingIntervalSeconds), _processingTokenSource.Token);
        }
      }
      catch (OperationCanceledException)
      {
        _logger.LogInformation("Batch processing task was cancelled");
        tcs.TrySetCanceled();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Fatal error in batch processing task");
        tcs.TrySetException(ex);
      }
      finally
      {
        _isProcessing = false;
      }
    }, _processingTokenSource.Token);

    // タスクの開始を待機（タスクそのものの完了ではなく、初期化完了を待つ）
    await tcs.Task;
  }

  /// <summary>
  /// バッチ処理を停止します
  /// </summary>
  public virtual async Task StopAsync(CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorService));

    if (!_isProcessing)
    {
      _logger.LogWarning("Batch processor is not running");
      return;
    }

    _logger.LogInformation("Stopping batch processor");

    try
    {
      // 処理中のタスクをキャンセル
      if (!_processingTokenSource.IsCancellationRequested)
      {
        _processingTokenSource.Cancel();
      }

      // 最後のバッチを処理
      if (_processingTask != null)
      {
        // 外部のキャンセルトークンを考慮
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        
        try
        {
          await ProcessBatchAsync(true, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
          // タイムアウトまたはキャンセルは正常
          _logger.LogWarning("Final batch processing was cancelled or timed out");
        }

        // タスクの完了を待機（タイムアウト付き）
        var completionTask = _processingTask;
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        var completedTask = await Task.WhenAny(completionTask, timeoutTask);
        
        if (completedTask == timeoutTask && !completionTask.IsCompleted)
        {
          _logger.LogWarning("Batch processing task did not complete within timeout");
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stopping batch processor");
    }
    finally
    {
      _isProcessing = false;
    }
  }

  /// <summary>
  /// バッチが満杯かどうかを判断します
  /// </summary>
  /// <returns>バッチが満杯の場合はtrue</returns>
  public virtual bool IsBatchFull()
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorService));

    // スレッドセーフな読み取り
    _queueLock.EnterReadLock();
    try
    {
      return GetBatchSizeBytes() >= _config.MaxBatchSizeBytes ||
             _batchQueue.Count >= _config.MaxBatchItems;
    }
    finally
    {
      _queueLock.ExitReadLock();
    }
  }

  /// <summary>
  /// 現在のバッチのアイテム数を取得します
  /// </summary>
  /// <returns>アイテム数</returns>
  public virtual int GetBatchItemCount()
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorService));

    // スレッドセーフな読み取り
    _queueLock.EnterReadLock();
    try
    {
      return _batchQueue.Count;
    }
    finally
    {
      _queueLock.ExitReadLock();
    }
  }

  /// <summary>
  /// 現在のバッチのサイズ（バイト）を取得します
  /// </summary>
  /// <returns>サイズ（バイト）</returns>
  public virtual long GetBatchSizeBytes()
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorService));

    // アトミックな読み取り
    return Interlocked.Read(ref _currentBatchSizeBytes);
  }

  /// <summary>
  /// エントリのサイズを推定します
  /// </summary>
  /// <param name="entry">ログエントリ</param>
  /// <returns>推定サイズ（バイト）</returns>
  private long EstimateEntrySize(LogEntry entry)
  {
    if (entry == null)
      return 0;

    // 簡易的なサイズ推定
    // 実際のプロジェクトではより正確な推定が必要かもしれません
    long size = 0;

    size += entry.Id?.Length ?? 0;
    size += entry.DeviceId?.Length ?? 0;
    size += entry.Message?.Length ?? 0;
    size += entry.Level?.Length ?? 0;
    size += entry.SourceFile?.Length ?? 0;
    size += 100; // その他のプロパティやオーバーヘッドの推定値

    return size;
  }

  /// <summary>
  /// リソースを非同期で解放します
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    await DisposeAsyncCore();
    Dispose(false);
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// リソースを非同期で解放する内部実装
  /// </summary>
  protected virtual async ValueTask DisposeAsyncCore()
  {
    if (_isDisposed)
      return;

    // 処理中のタスクを停止
    if (_isProcessing)
    {
      await StopAsync(CancellationToken.None);
    }
  }

  /// <summary>
  /// リソースを解放します
  /// </summary>
  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// リソースを解放します
  /// </summary>
  /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
  protected virtual void Dispose(bool disposing)
  {
    if (_isDisposed)
      return;

    if (disposing)
    {
      // マネージドリソースの破棄
      _processingTokenSource.Dispose();
      _batchSizeLock.Dispose();
      _queueLock.Dispose();
      (_processingLock as IDisposable)?.Dispose();
    }

    _isDisposed = true;
  }
}
