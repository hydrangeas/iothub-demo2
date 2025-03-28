using MachineLog.Collector.Models;
using MachineLog.Common.Models;
using MachineLog.Common.Utilities;
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
  private readonly AsyncLock _processingLock;
  // private readonly SemaphoreSlim _batchSizeLock = new SemaphoreSlim(1, 1); // 削除
  // private readonly ReaderWriterLockSlim _queueLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion); // 削除

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
    _processingLock = new AsyncLock();

    // デッドロック検出を有効化 -> 削除
    // DeadlockDetector.Enable();
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

    // エントリをキューに追加（ConcurrentQueueはスレッドセーフ）
    _batchQueue.Enqueue(entry);

    // バッチサイズを更新（アトミック操作）
    Interlocked.Add(ref _currentBatchSizeBytes, EstimateEntrySize(entry)); // 変更

    _logger.LogDebug("Added entry to batch. Current count: {Count}, Size: {Size} bytes",
        _batchQueue.Count, Interlocked.Read(ref _currentBatchSizeBytes)); // 変更

    return true;
  }

  /// <summary>
  /// 複数のエントリをバッチに追加します
  /// </summary>
  public virtual async Task<bool> AddRangeToBatchAsync(IEnumerable<LogEntry> entries, CancellationToken cancellationToken = default)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    if (entries == null)
    {
      _logger.LogWarning("Null entries collection was provided to batch processor");
      return false;
    }

    // ToList()を1回だけ呼び出し、null要素を事前にフィルタリング
    var validEntries = entries.Where(e => e != null).ToList();
    if (validEntries.Count == 0)
    {
      return true;
    }

    // バッチが満杯の場合は処理を実行
    if (IsBatchFull())
    {
      _logger.LogInformation("Batch is full, processing before adding new entries");
      await ProcessBatchAsync(false, cancellationToken).ConfigureAwait(false);
    }

    // 事前に合計サイズを計算
    long totalAddedSize = validEntries.Sum(e => EstimateEntrySize(e));

    // すべてのエントリを一度に追加
    foreach (var entry in validEntries)
    {
      _batchQueue.Enqueue(entry);
    }

    // バッチサイズを更新（アトミック操作で1回だけ実行）
    Interlocked.Add(ref _currentBatchSizeBytes, totalAddedSize);

    _logger.LogDebug("Added {Count} entries to batch. Current count: {TotalCount}, Size: {Size} bytes",
        validEntries.Count, _batchQueue.Count, Interlocked.Read(ref _currentBatchSizeBytes));

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
      BatchSizeBytes = Interlocked.Read(ref _currentBatchSizeBytes)
    };

    // 早期リターン：キャンセルの場合
    if (cancellationToken.IsCancellationRequested)
    {
      _logger.LogInformation("バッチ処理がキャンセルされました");
      result.Success = false;
      result.ErrorMessage = "処理がキャンセルされました";
      return result;
    }

    // 非同期ロックを使用して同時実行を防止
    using (await _processingLock.LockAsync(cancellationToken).ConfigureAwait(false))
    {
      var stopwatch = Stopwatch.StartNew();

      try
      {
        // バッチが空で、強制処理でない場合は何もしない
        if (_batchQueue.IsEmpty && !force)
        {
          _logger.LogDebug("バッチは空です。処理するものがありません");
          return result;
        }

        // キャンセルの再確認
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("バッチを処理します: {Count}件, {Size}バイト",
            _batchQueue.Count, Interlocked.Read(ref _currentBatchSizeBytes));

        // バッチ内のアイテムを取得し、サイズをリセット
        var itemsToProcess = DequeueAllBatchItems();
        result.BatchSizeBytes = Interlocked.Exchange(ref _currentBatchSizeBytes, 0);

        // 処理するアイテムがある場合
        if (itemsToProcess.Count > 0)
        {
          result.ProcessedItems = itemsToProcess.Count;

          // 並列処理の最適化（環境に応じた並列度を設定）
          int maxDegreeOfParallelism = CalculateOptimalParallelism();

          // アイテムを並列処理（IoTHubサービスへの送信などの実際の処理）
          await ProcessItemsInParallelAsync(itemsToProcess, maxDegreeOfParallelism, cancellationToken)
              .ConfigureAwait(false);
        }
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        _logger.LogInformation("バッチ処理がキャンセルされました");
        result.Success = false;
        result.ErrorMessage = "処理がキャンセルされました";
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "バッチ処理中にエラーが発生しました");
        result.Success = false;
        result.ErrorMessage = ex.Message;
        result.Exception = ex;
      }
      finally
      {
        stopwatch.Stop();
        result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
      }
    }

    return result;
  }

  /// <summary>
  /// アイテムを並列処理します
  /// </summary>
  private async Task ProcessItemsInParallelAsync(List<LogEntry> itemsToProcess, int maxDegreeOfParallelism,
    CancellationToken cancellationToken)
  {
    await SynchronizationUtility.ParallelForEachAsync(
        itemsToProcess,
        async (item, ct) =>
        {
          // TODO: 実際の処理をここに実装（IoT Hubに送信など）
          // この部分は実際の実装に合わせて修正する必要があります
          await Task.Delay(1, ct).ConfigureAwait(false);
          return true;
        },
        maxDegreeOfParallelism,
        cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// 環境に応じた最適な並列処理数を計算します
  /// </summary>
  private int CalculateOptimalParallelism()
  {
    // CPU数と設定された最大並列度の小さい方を選択
    // 2コア以下の場合は2、それ以外は最大でCPU数の半分（最大4）を使用
    int cpuCount = Environment.ProcessorCount;
    int defaultParallelism = cpuCount <= 2 ? 2 : Math.Min(cpuCount / 2, 4);

    // 設定から並列度を取得（設定されていない場合はデフォルト値を使用）
    return Math.Max(1, defaultParallelism);
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
      _logger.LogWarning("バッチプロセッサは既に実行中です");
      return;
    }

    _logger.LogInformation("バッチプロセッサを開始します");

    // 同時変更を防止するため、ロックを使用
    using (await _processingLock.LockAsync(cancellationToken).ConfigureAwait(false))
    {
      // ロック内で再確認（競合条件を防止）
      if (_isProcessing)
      {
        _logger.LogWarning("バッチプロセッサは既に実行中です（ロック内で再確認）");
        return;
      }

      _isProcessing = true;
    }

    // 非同期メソッドの待機を確実にするため、TaskCompletionSourceを使用
    var tcs = new TaskCompletionSource<bool>();

    // 定期的なバッチ処理タスクを開始
    _processingTask = Task.Run(async () =>
    {
      try
      {
        // 開始をシグナル
        tcs.TrySetResult(true);

        // キャンセルされるまで定期的に処理を実行
        while (!_processingTokenSource.Token.IsCancellationRequested)
        {
          try
          {
            await ProcessBatchAsync(true, _processingTokenSource.Token).ConfigureAwait(false);
          }
          catch (OperationCanceledException) when (_processingTokenSource.Token.IsCancellationRequested)
          {
            // キャンセルは正常な動作
            break;
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "バッチ処理ループ内でエラーが発生しました");
            // エラーが発生しても処理を継続（耐障害性のため）
          }

          try
          {
            // 次の処理までの待機（キャンセル対応）
            await Task.Delay(
                TimeSpan.FromSeconds(_config.ProcessingIntervalSeconds),
                _processingTokenSource.Token).ConfigureAwait(false);
          }
          catch (OperationCanceledException) when (_processingTokenSource.Token.IsCancellationRequested)
          {
            // キャンセルは正常な動作
            break;
          }
        }
      }
      catch (OperationCanceledException)
      {
        _logger.LogInformation("バッチ処理タスクがキャンセルされました");
        tcs.TrySetCanceled();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "バッチ処理タスクで致命的なエラーが発生しました");
        tcs.TrySetException(ex);
      }
      finally
      {
        // 状態を更新（ロックなしでも安全な単純な代入操作）
        _isProcessing = false;
      }
    }, _processingTokenSource.Token);

    // タスクの開始を待機（タスクそのものの完了ではなく、初期化完了を待つ）
    await tcs.Task.ConfigureAwait(false);
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
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    // サイズとアイテム数を取得
    long batchSize = GetBatchSizeBytes();
    int itemCount = GetBatchItemCount();

    // いずれかの上限に達していれば満杯と判断
    return batchSize >= _config.MaxBatchSizeBytes ||
           itemCount >= _config.MaxBatchItems;
  }

  /// <summary>
  /// 現在のバッチのアイテム数を取得します
  /// </summary>
  /// <returns>アイテム数</returns>
  public virtual int GetBatchItemCount()
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    // ConcurrentQueueのCountプロパティはスレッドセーフ
    return _batchQueue.Count; // 変更
  }

  /// <summary>
  /// 現在のバッチのサイズ（バイト）を取得します (テスト用に再追加)
  /// </summary>
  /// <returns>サイズ（バイト）</returns>
  public virtual long GetBatchSizeBytes()
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();
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

      // 残りのバッチを同期的に処理 -> 非推奨のため簡略化
      // try { ... Task.Run().Wait() ... } catch ...

      // リソースの解放
      _processingTokenSource.Dispose();
      // _batchSizeLock.Dispose(); // 削除
      // _queueLock.Dispose(); // 削除
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
      // _batchSizeLock.Dispose(); // 削除
      // _queueLock.Dispose(); // 削除
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

  /// <summary>
  /// キューから現在のバッチ内のすべてのアイテムを取得し、キューを空にします。
  /// </summary>
  /// <returns>処理対象のログエントリのリスト</returns>
  private List<LogEntry> DequeueAllBatchItems()
  {
    var itemsToProcess = new List<LogEntry>();
    while (_batchQueue.TryDequeue(out var item))
    {
      if (item != null)
      {
        itemsToProcess.Add(item);
      }
    }
    return itemsToProcess;
  }
}
