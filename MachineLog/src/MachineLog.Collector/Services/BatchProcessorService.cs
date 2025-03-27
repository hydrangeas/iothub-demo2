using MachineLog.Collector.Models;
using MachineLog.Common.Models;
using MachineLog.Common.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace MachineLog.Collector.Services;

/// <summary>
/// バッチ処理サービスの実装
/// </summary>
public class BatchProcessorService : AsyncDisposableBase<BatchProcessorService>, IBatchProcessorService
{
  private readonly ILogger<BatchProcessorService> _logger;
  private readonly BatchConfig _config;
  private readonly IIoTHubService _iotHubService;
  private readonly ConcurrentQueue<LogEntry> _batchQueue;
  private readonly CancellationTokenSource _processingTokenSource;
  private Task? _processingTask;
  private long _currentBatchSizeBytes;
  private bool _isProcessing;
  private readonly SemaphoreSlim _processingLock = new SemaphoreSlim(1, 1);

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="config">バッチ設定</param>
  /// <param name="iotHubService">IoT Hubサービス</param>
  public BatchProcessorService(
      ILogger<BatchProcessorService> logger,
      IOptions<BatchConfig> config,
      IIoTHubService iotHubService) : base(true) // リソースマネージャーに登録
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    _iotHubService = iotHubService ?? throw new ArgumentNullException(nameof(iotHubService));
    _batchQueue = new ConcurrentQueue<LogEntry>();
    _processingTokenSource = new CancellationTokenSource();
    _currentBatchSizeBytes = 0;
    _isProcessing = false;
  }

  /// <summary>
  /// エントリをバッチに追加します
  /// </summary>
  public virtual async Task<bool> AddToBatchAsync(LogEntry entry, CancellationToken cancellationToken = default)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

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

    // エントリをキューに追加
    _batchQueue.Enqueue(entry);
    Interlocked.Add(ref _currentBatchSizeBytes, EstimateEntrySize(entry));

    _logger.LogDebug("Added entry to batch. Current count: {Count}, Size: {Size} bytes",
        _batchQueue.Count, _currentBatchSizeBytes);

    return true;
  }

  /// <summary>
  /// 現在のバッチを処理します
  /// </summary>
  public virtual async Task<BatchProcessingResult> ProcessBatchAsync(bool force = false, CancellationToken cancellationToken = default)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    var result = new BatchProcessingResult
    {
      Success = true,
      ProcessedItems = 0,
      BatchSizeBytes = _currentBatchSizeBytes
    };

    // セマフォを使用して同時実行を防止
    await _processingLock.WaitAsync(cancellationToken);

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

      // ここでバッチ処理のロジックを実装
      // 例: ファイルに書き出し、IoT Hubにアップロードなど

      // バッチをクリア
      // キューをクリアする前にカウントを取得して、実際に処理されたバッチを正確に反映
      result.ProcessedItems = _batchQueue.Count;
      // ConcurrentQueueにはClear()メソッドがないため、ループでキューをクリア
      while (_batchQueue.TryDequeue(out _)) { }
      Interlocked.Exchange(ref _currentBatchSizeBytes, 0);
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

      // セマフォを解放
      _processingLock.Release();
    }

    return result;
  }

  /// <summary>
  /// バッチ処理を開始します
  /// </summary>
  public virtual async Task StartAsync(CancellationToken cancellationToken = default)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

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
          await ProcessBatchAsync(true, _processingTokenSource.Token);
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
        _logger.LogError(ex, "Error in batch processing task");
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
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    if (!_isProcessing)
    {
      _logger.LogWarning("Batch processor is not running");
      return;
    }

    _logger.LogInformation("Stopping batch processor");

    try
    {
      // 処理中のタスクをキャンセル
      _processingTokenSource.Cancel();

      // 最後のバッチを処理
      if (_processingTask != null)
      {
        await ProcessBatchAsync(true, cancellationToken);

        // タスクが完了するまでのタイムアウトを設定して待機
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        await Task.WhenAny(_processingTask, timeoutTask);
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
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    return GetBatchSizeBytes() >= _config.MaxBatchSizeBytes ||
           GetBatchItemCount() >= _config.MaxBatchItems;
  }

  /// <summary>
  /// 現在のバッチのアイテム数を取得します
  /// </summary>
  /// <returns>アイテム数</returns>
  public virtual int GetBatchItemCount()
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    return _batchQueue.Count;
  }

  /// <summary>
  /// 現在のバッチのサイズ（バイト）を取得します
  /// </summary>
  /// <returns>サイズ（バイト）</returns>
  public virtual long GetBatchSizeBytes()
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    return _currentBatchSizeBytes;
  }

  /// <summary>
  /// エントリのサイズを推定します
  /// </summary>
  /// <param name="entry">ログエントリ</param>
  /// <returns>推定サイズ（バイト）</returns>
  private long EstimateEntrySize(LogEntry entry)
  {
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
  /// リソースのサイズを推定します
  /// </summary>
  /// <returns>推定サイズ（バイト単位）</returns>
  protected override long EstimateResourceSize()
  {
    // バッチプロセッサのリソースサイズを推定
    return 1 * 1024 * 1024; // 1MB
  }

  /// <summary>
  /// マネージドリソースを解放します
  /// </summary>
  protected override void ReleaseManagedResources()
  {
    _logger.LogInformation("BatchProcessorServiceのリソースを解放します");

    try
    {
      // 処理中のタスクをキャンセル
      if (!_processingTokenSource.IsCancellationRequested)
      {
        _processingTokenSource.Cancel();
      }

      // 残りのバッチを同期的に処理
      try
      {
        if (_isProcessing && _processingTask != null)
        {
          // 同期的にバッチを処理し、短いタイムアウトを設定
          ProcessBatchAsync(true, CancellationToken.None).GetAwaiter().GetResult();

          // タスクが完了するまで短時間待機
          if (!_processingTask.Wait(TimeSpan.FromSeconds(3)))
          {
            _logger.LogWarning("タスク完了待機がタイムアウトしました");
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "残りのバッチの解放中にエラーが発生しました");
      }

      // リソースの解放
      _processingTokenSource.Dispose();
      _processingLock.Dispose();

      // キューをクリア
      while (_batchQueue.TryDequeue(out _)) { }
      Interlocked.Exchange(ref _currentBatchSizeBytes, 0);

      _isProcessing = false;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "BatchProcessorServiceのリソース解放中にエラーが発生しました");
    }

    // 基底クラスの処理を呼び出す
    base.ReleaseManagedResources();
  }

  /// <summary>
  /// マネージドリソースを非同期で解放します
  /// </summary>
  protected override async ValueTask ReleaseManagedResourcesAsync()
  {
    _logger.LogInformation("BatchProcessorServiceのリソースを非同期で解放します");

    try
    {
      // 処理中のタスクをキャンセル
      if (!_processingTokenSource.IsCancellationRequested)
      {
        _processingTokenSource.Cancel();
      }

      // 残りのバッチを非同期で処理
      try
      {
        if (_isProcessing && _processingTask != null)
        {
          // 残りのバッチを短いタイムアウトで処理
          using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
          await ProcessBatchAsync(true, cts.Token).ConfigureAwait(false);

          // タスクが完了するまで待機
          var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
          await Task.WhenAny(_processingTask, timeoutTask).ConfigureAwait(false);

          if (!_processingTask.IsCompleted)
          {
            _logger.LogWarning("非同期タスクの完了待機がタイムアウトしました");
          }
        }
      }
      catch (OperationCanceledException)
      {
        // タイムアウトは正常な動作
        _logger.LogInformation("バッチ処理のキャンセルが成功しました");
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "残りのバッチの非同期解放中にエラーが発生しました");
      }

      // リソースの解放
      _processingTokenSource.Dispose();
      _processingLock.Dispose();

      // キューをクリア
      while (_batchQueue.TryDequeue(out _)) { }
      Interlocked.Exchange(ref _currentBatchSizeBytes, 0);

      _isProcessing = false;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "BatchProcessorServiceのリソース非同期解放中にエラーが発生しました");
    }

    // 基底クラスの処理を呼び出す
    await base.ReleaseManagedResourcesAsync().ConfigureAwait(false);
  }
}
