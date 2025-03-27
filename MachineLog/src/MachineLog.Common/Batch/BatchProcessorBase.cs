using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MachineLog.Common.Batch;

/// <summary>
/// バッチ処理の基本クラス
/// </summary>
/// <typeparam name="T">バッチ処理の対象となる型</typeparam>
public abstract class BatchProcessorBase<T> : IBatchProcessor<T>, IDisposable, IAsyncDisposable where T : class
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
  private Task _processingTask;

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
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>追加が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> AddAsync(T item, CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorBase<T>));

    if (item == null)
      throw new ArgumentNullException(nameof(item));

    _lastItemAddedTime = DateTime.UtcNow;
    return await _queue.EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// 複数のアイテムをバッチに追加する
  /// </summary>
  /// <param name="items">追加するアイテムのコレクション</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>追加が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> AddRangeAsync(IEnumerable<T> items, CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorBase<T>));

    if (items == null)
      throw new ArgumentNullException(nameof(items));

    _lastItemAddedTime = DateTime.UtcNow;
    return await _queue.EnqueueRangeAsync(items, cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// 現在のバッチを強制的に処理する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>処理が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorBase<T>));

    return await ProcessBatchAsync(cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// バッチ処理を開始する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>開始が成功したかどうかを示す非同期タスク</returns>
  public Task<bool> StartAsync(CancellationToken cancellationToken = default)
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

    // バッチ処理ループを開始（外部のキャンセルトークンと内部のトークンをリンク）
    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken))
    {
        _processingTask = Task.Run(() => ProcessBatchLoopAsync(linkedCts.Token), linkedCts.Token);
    }

    return Task.FromResult(true);
  }

  /// <summary>
  /// バッチ処理を停止する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>停止が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorBase<T>));

    // タイマーを停止
    _batchTimer.Change(Timeout.Infinite, Timeout.Infinite);
    _idleTimer.Change(Timeout.Infinite, Timeout.Infinite);

    // 残りのバッチを処理
    await ProcessBatchAsync(cancellationToken).ConfigureAwait(false);

    // キャンセルトークンをキャンセル
    _cts.Cancel();

    // バッチ処理ループが完全に終了するまで待機
    if (_processingTask != null)
    {
      try
      {
        // タスクが完了するまで待機（外部のキャンセルトークンも考慮）
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        var completedTask = await Task.WhenAny(_processingTask, timeoutTask).ConfigureAwait(false);
        
        if (completedTask == timeoutTask && !_processingTask.IsCompleted)
        {
          // タイムアウトした場合はログに記録
          Console.Error.WriteLine("バッチ処理ループの停止がタイムアウトしました");
        }
      }
      catch (OperationCanceledException)
      {
        // キャンセルは正常な動作
      }
    }

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
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>処理が成功したかどうかを示す非同期タスク</returns>
  protected abstract Task<bool> ProcessBatchItemsAsync(List<T> batch, CancellationToken cancellationToken = default);

  /// <summary>
  /// バッチ処理ループを実行する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  private async Task ProcessBatchLoopAsync(CancellationToken cancellationToken)
  {
    try
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        try
        {
          // キューからアイテムを取得
          var items = await _queue.DequeueMultipleAsync(
              _options.MaxBatchCount,
              _options.IdleTimeoutInMilliseconds,
              cancellationToken).ConfigureAwait(false);

          if (items.Count > 0)
          {
            await _batchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
              _currentBatch.AddRange(items);

              // バッチサイズまたはカウントが上限に達した場合、処理を実行
              if (GetCurrentBatchSize() >= _options.MaxBatchSizeInBytes ||
                  GetCurrentBatchCount() >= _options.MaxBatchCount)
              {
                await ProcessCurrentBatchAsync(cancellationToken).ConfigureAwait(false);
              }
            }
            finally
            {
              _batchLock.Release();
            }
          }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          // キャンセルされた場合は正常終了
          break;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
          // キャンセルされていない場合のみエラーログを出力
          Console.Error.WriteLine($"バッチ処理ループでエラーが発生しました: {ex}");
          
          // 短い遅延を入れて連続エラーを防止
          await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
      }
    }
    catch (OperationCanceledException)
    {
      // キャンセルされた場合は正常終了
    }
    catch (Exception ex)
    {
      // 予期しないエラー
      Console.Error.WriteLine($"バッチ処理ループで予期しないエラーが発生しました: {ex}");
    }
  }

  /// <summary>
  /// バッチタイマーのコールバック
  /// </summary>
  private async void ProcessBatchTimerCallback(object state)
  {
    try
    {
      // タイマーコールバックでは短いタイムアウトを設定
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
      await ProcessBatchAsync(cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // タイムアウトは正常
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
        // タイマーコールバックでは短いタイムアウトを設定
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await ProcessBatchAsync(cts.Token).ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException)
    {
      // タイムアウトは正常
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
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  private async Task<bool> ProcessBatchAsync(CancellationToken cancellationToken = default)
  {
    if (_isProcessing || GetCurrentBatchCount() == 0)
      return true;

    await _batchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      _isProcessing = true;
      return await ProcessCurrentBatchAsync(cancellationToken).ConfigureAwait(false);
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
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  private async Task<bool> ProcessCurrentBatchAsync(CancellationToken cancellationToken = default)
  {
    if (_currentBatch.Count == 0)
      return true;

    var batchToProcess = new List<T>(_currentBatch);
    _currentBatch.Clear();

    return await ProcessBatchItemsAsync(batchToProcess, cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// バッチ処理の非同期ストリームを取得する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>バッチ処理の非同期ストリーム</returns>
  public async IAsyncEnumerable<T> GetItemsAsAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    if (_isDisposed)
      throw new ObjectDisposedException(nameof(BatchProcessorBase<T>));

    await foreach (var item in _queue.GetAsyncEnumerable(cancellationToken))
    {
      if (cancellationToken.IsCancellationRequested)
        yield break;
        
      yield return item;
    }
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
  /// リソースを非同期で解放する
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    await DisposeAsyncCore().ConfigureAwait(false);
    Dispose(false);
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// 非同期でリソースを解放する内部実装
  /// </summary>
  protected virtual async ValueTask DisposeAsyncCore()
  {
    if (_isDisposed)
      return;

    // 処理中のタスクを停止
    if (!_cts.IsCancellationRequested)
    {
      _cts.Cancel();
    }

    // 残りのバッチを処理
    try
    {
      using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      await ProcessBatchAsync(timeoutCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // タイムアウトは正常
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"非同期破棄中にバッチ処理でエラーが発生しました: {ex}");
    }
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
      // タイマーを停止
      _batchTimer.Change(Timeout.Infinite, Timeout.Infinite);
      _idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
      
      // キャンセルトークンをキャンセル
      if (!_cts.IsCancellationRequested)
      {
        _cts.Cancel();
      }
      
      // マネージドリソースの破棄
      _batchTimer.Dispose();
      _idleTimer.Dispose();
      _batchLock.Dispose();
      _cts.Dispose();
      _queue.Dispose();
    }

    _isDisposed = true;
  }
}
