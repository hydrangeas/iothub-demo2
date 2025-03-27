using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MachineLog.Common.Batch;

/// <summary>
/// バッチ処理の基本クラス
/// </summary>
/// <typeparam name="T">バッチ処理の対象となる型</typeparam>
public abstract class BatchProcessorBase<T> : IBatchProcessor<T>, IDisposable where T : class
{
  private readonly BatchQueue<T> _queue;
  private readonly BatchProcessorOptions _options;
  private readonly List<T> _currentBatch;
  private readonly SemaphoreSlim _batchLock;
  private readonly Timer _batchTimer;
  private readonly Timer _idleTimer;
  private readonly CancellationTokenSource _cts;
  private bool _isProcessing;
  private bool _isDisposed;
  private DateTime _lastItemAddedTime;

  /// <summary>
  /// バッチ処理の基本クラスを初期化する
  /// </summary>
  /// <param name="options">バッチ処理のオプション</param>
  protected BatchProcessorBase(BatchProcessorOptions options)
  {
    _options = options ?? BatchProcessorOptions.Default;
    _queue = new BatchQueue<T>(_options.BatchQueueCapacity);
    _currentBatch = new List<T>();
    _batchLock = new SemaphoreSlim(1, 1);
    _cts = new CancellationTokenSource();
    _lastItemAddedTime = DateTime.UtcNow;

    // バッチ処理タイマーの初期化
    _batchTimer = new Timer(
        ProcessBatchTimerCallback,
        null,
        Timeout.Infinite,
        Timeout.Infinite);

    // アイドルタイマーの初期化
    _idleTimer = new Timer(
        IdleTimeoutCallback,
        null,
        Timeout.Infinite,
        Timeout.Infinite);
  }

  /// <summary>
  /// アイテムをバッチに追加する
  /// </summary>
  /// <param name="item">追加するアイテム</param>
  /// <returns>追加が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> AddAsync(T item)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorBase<T>));

    if (item == null)
      throw new ArgumentNullException(nameof(item));

    _lastItemAddedTime = DateTime.UtcNow;
    return await _queue.EnqueueAsync(item);
  }

  /// <summary>
  /// 複数のアイテムをバッチに追加する
  /// </summary>
  /// <param name="items">追加するアイテムのコレクション</param>
  /// <returns>追加が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> AddRangeAsync(IEnumerable<T> items)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorBase<T>));

    if (items == null)
      throw new ArgumentNullException(nameof(items));

    _lastItemAddedTime = DateTime.UtcNow;
    return await _queue.EnqueueRangeAsync(items);
  }

  /// <summary>
  /// 現在のバッチを強制的に処理する
  /// </summary>
  /// <returns>処理が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> FlushAsync()
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorBase<T>));

    return await ProcessBatchAsync();
  }

  /// <summary>
  /// バッチ処理を開始する
  /// </summary>
  /// <returns>開始が成功したかどうかを示す非同期タスク</returns>
  public Task<bool> StartAsync()
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorBase<T>));

    // バッチ処理タイマーを開始
    _batchTimer.Change(
        _options.BatchIntervalInMilliseconds,
        _options.BatchIntervalInMilliseconds);

    // アイドルタイマーを開始
    _idleTimer.Change(
        _options.IdleTimeoutInMilliseconds,
        _options.IdleTimeoutInMilliseconds);

    // バッチ処理ループを開始
    Task.Run(ProcessBatchLoopAsync);

    return Task.FromResult(true);
  }

  /// <summary>
  /// バッチ処理を停止する
  /// </summary>
  /// <returns>停止が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> StopAsync()
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorBase<T>));

    // タイマーを停止
    _batchTimer.Change(Timeout.Infinite, Timeout.Infinite);
    _idleTimer.Change(Timeout.Infinite, Timeout.Infinite);

    // 残りのバッチを処理
    await ProcessBatchAsync();

    // キャンセルトークンをキャンセル
    _cts.Cancel();

    return true;
  }

  /// <summary>
  /// 現在のバッチサイズを取得する
  /// </summary>
  /// <returns>現在のバッチサイズ（バイト単位）</returns>
  public int GetCurrentBatchSize()
  {
    return CalculateBatchSize(_currentBatch);
  }

  /// <summary>
  /// 現在のバッチのエントリ数を取得する
  /// </summary>
  /// <returns>現在のバッチのエントリ数</returns>
  public int GetCurrentBatchCount()
  {
    return _currentBatch.Count;
  }

  /// <summary>
  /// バッチ処理のオプションを取得する
  /// </summary>
  /// <returns>バッチ処理のオプション</returns>
  public BatchProcessorOptions GetOptions()
  {
    return _options;
  }

  /// <summary>
  /// バッチのサイズを計算する
  /// </summary>
  /// <param name="batch">サイズを計算するバッチ</param>
  /// <returns>バッチのサイズ（バイト単位）</returns>
  protected abstract int CalculateBatchSize(List<T> batch);

  /// <summary>
  /// バッチを処理する
  /// </summary>
  /// <param name="batch">処理するバッチ</param>
  /// <returns>処理が成功したかどうかを示す非同期タスク</returns>
  protected abstract Task<bool> ProcessBatchItemsAsync(List<T> batch);

  /// <summary>
  /// バッチ処理ループを実行する
  /// </summary>
  private async Task ProcessBatchLoopAsync()
  {
    try
    {
      while (!_cts.Token.IsCancellationRequested)
      {
        // キューからアイテムを取得
        var items = await _queue.DequeueMultipleAsync(
            _options.MaxBatchCount,
            _options.IdleTimeoutInMilliseconds);

        if (items.Count > 0)
        {
          await _batchLock.WaitAsync(_cts.Token);
          try
          {
            _currentBatch.AddRange(items);

            // バッチサイズまたはカウントが上限に達した場合、処理を実行
            if (GetCurrentBatchSize() >= _options.MaxBatchSizeInBytes ||
                GetCurrentBatchCount() >= _options.MaxBatchCount)
            {
              await ProcessCurrentBatchAsync();
            }
          }
          finally
          {
            _batchLock.Release();
          }
        }
      }
    }
    catch (OperationCanceledException)
    {
      // キャンセルされた場合は正常終了
    }
    catch (Exception ex)
    {
      // エラーログ出力など
      Console.Error.WriteLine($"バッチ処理ループでエラーが発生しました: {ex}");
    }
  }

  /// <summary>
  /// バッチタイマーのコールバック
  /// </summary>
  private async void ProcessBatchTimerCallback(object state)
  {
    try
    {
      await ProcessBatchAsync();
    }
    catch (Exception ex)
    {
      // エラーログ出力など
      Console.Error.WriteLine($"バッチ処理タイマーコールバックでエラーが発生しました: {ex}");
    }
  }

  /// <summary>
  /// アイドルタイムアウトのコールバック
  /// </summary>
  private async void IdleTimeoutCallback(object state)
  {
    try
    {
      var idleTime = DateTime.UtcNow - _lastItemAddedTime;
      if (idleTime.TotalMilliseconds >= _options.IdleTimeoutInMilliseconds && GetCurrentBatchCount() > 0)
      {
        await ProcessBatchAsync();
      }
    }
    catch (Exception ex)
    {
      // エラーログ出力など
      Console.Error.WriteLine($"アイドルタイムアウトコールバックでエラーが発生しました: {ex}");
    }
  }

  /// <summary>
  /// バッチを処理する
  /// </summary>
  private async Task<bool> ProcessBatchAsync()
  {
    if (_isProcessing || GetCurrentBatchCount() == 0)
      return true;

    await _batchLock.WaitAsync();
    try
    {
      _isProcessing = true;
      return await ProcessCurrentBatchAsync();
    }
    finally
    {
      _isProcessing = false;
      _batchLock.Release();
    }
  }

  /// <summary>
  /// 現在のバッチを処理する
  /// </summary>
  private async Task<bool> ProcessCurrentBatchAsync()
  {
    if (_currentBatch.Count == 0)
      return true;

    var batchToProcess = new List<T>(_currentBatch);
    _currentBatch.Clear();

    return await ProcessBatchItemsAsync(batchToProcess);
  }

  /// <summary>
  /// リソースを解放する
  /// </summary>
  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// リソースを解放する
  /// </summary>
  /// <param name="disposing">マネージドリソースを解放するかどうか</param>
  protected virtual void Dispose(bool disposing)
  {
    if (_isDisposed)
      return;

    if (disposing)
    {
      _batchTimer.Dispose();
      _idleTimer.Dispose();
      _batchLock.Dispose();
      _cts.Dispose();
      _queue.Dispose();
    }

    _isDisposed = true;
  }
}
