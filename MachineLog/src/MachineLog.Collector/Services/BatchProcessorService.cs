using MachineLog.Collector.Models;
using MachineLog.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MachineLog.Collector.Services;

/// <summary>
/// バッチ処理サービスの実装
/// </summary>
public class BatchProcessorService : IBatchProcessorService, IDisposable
{
  private readonly ILogger<BatchProcessorService> _logger;
  private readonly BatchConfig _config;
  private readonly IIoTHubService _iotHubService;
  private readonly ConcurrentQueue<LogEntry> _batchQueue;
  private readonly CancellationTokenSource _processingTokenSource;
  private Task? _processingTask;
  private long _currentBatchSizeBytes;
  private bool _isProcessing;

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
  }

  /// <summary>
  /// エントリをバッチに追加します
  /// </summary>
  public virtual async Task<bool> AddToBatchAsync(LogEntry entry, CancellationToken cancellationToken = default)
  {
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
    var result = new BatchProcessingResult
    {
      Success = true,
      ProcessedItems = 0,
      BatchSizeBytes = _currentBatchSizeBytes
    };

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
    }

    return result;
  }

  /// <summary>
  /// バッチ処理を開始します
  /// </summary>
  public virtual async Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (_isProcessing)
    {
      _logger.LogWarning("Batch processor is already running");
      return;
    }

    _logger.LogInformation("Starting batch processor");
    _isProcessing = true;

    // 定期的なバッチ処理タスクを開始
    _processingTask = Task.Run(async () =>
    {
      try
      {
        while (!_processingTokenSource.Token.IsCancellationRequested)
        {
          await ProcessBatchAsync(true, _processingTokenSource.Token);
          await Task.Delay(TimeSpan.FromSeconds(_config.ProcessingIntervalSeconds), _processingTokenSource.Token);
        }
      }
      catch (OperationCanceledException)
      {
        _logger.LogInformation("Batch processing task was cancelled");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in batch processing task");
      }
      finally
      {
        _isProcessing = false;
      }
    }, _processingTokenSource.Token);
  }

  /// <summary>
  /// バッチ処理を停止します
  /// </summary>
  public virtual async Task StopAsync(CancellationToken cancellationToken = default)
  {
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
        await _processingTask;
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
    return GetBatchSizeBytes() >= _config.MaxBatchSizeBytes ||
           GetBatchItemCount() >= _config.MaxBatchItems;
  }

  /// <summary>
  /// 現在のバッチのアイテム数を取得します
  /// </summary>
  /// <returns>アイテム数</returns>
  public virtual int GetBatchItemCount()
  {
    return _batchQueue.Count;
  }

  /// <summary>
  /// 現在のバッチのサイズ（バイト）を取得します
  /// </summary>
  /// <returns>サイズ（バイト）</returns>
  public virtual long GetBatchSizeBytes()
  {
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
  /// リソースを破棄します
  /// </summary>
  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// リソースを破棄します
  /// </summary>
  /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
  protected virtual void Dispose(bool disposing)
  {
    if (disposing)
    {
      // マネージドリソースの破棄
      _processingTokenSource.Dispose();
    }
  }
}