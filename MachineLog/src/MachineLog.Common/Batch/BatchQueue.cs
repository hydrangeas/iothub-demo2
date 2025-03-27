using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MachineLog.Common.Batch;

/// <summary>
/// バッチキューを表すクラス
/// </summary>
/// <typeparam name="T">キューに格納する型</typeparam>
public class BatchQueue<T> : IDisposable where T : class
{
  private readonly Channel<T> _channel;
  private readonly int _capacity;
  private readonly CancellationTokenSource _cts;
  private int _count;
  private bool _disposed;

  /// <summary>
  /// バッチキューを初期化する
  /// </summary>
  /// <param name="capacity">キューの容量</param>
  public BatchQueue(int capacity)
  {
    _capacity = capacity;
    _cts = new CancellationTokenSource();

    var options = new BoundedChannelOptions(capacity)
    {
      FullMode = BoundedChannelFullMode.Wait,
      SingleReader = false,
      SingleWriter = false,
      AllowSynchronousContinuations = false // 非同期処理の最適化
    };

    _channel = Channel.CreateBounded<T>(options);
  }

  /// <summary>
  /// アイテムをキューに追加する
  /// </summary>
  /// <param name="item">追加するアイテム</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>追加が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> EnqueueAsync(T item, CancellationToken cancellationToken = default)
  {
    if (_disposed) throw new ObjectDisposedException(nameof(BatchQueue<T>));
    if (item == null) throw new ArgumentNullException(nameof(item));

    try
    {
      // 外部と内部のキャンセルトークンを連携
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
      
      await _channel.Writer.WriteAsync(item, linkedCts.Token).ConfigureAwait(false);
      Interlocked.Increment(ref _count);
      return true;
    }
    catch (OperationCanceledException)
    {
      return false;
    }
    catch (ChannelClosedException)
    {
      return false;
    }
  }

  /// <summary>
  /// 複数のアイテムをキューに追加する
  /// </summary>
  /// <param name="items">追加するアイテムのコレクション</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>追加が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> EnqueueRangeAsync(IEnumerable<T> items, CancellationToken cancellationToken = default)
  {
    if (_disposed) throw new ObjectDisposedException(nameof(BatchQueue<T>));
    if (items == null) throw new ArgumentNullException(nameof(items));

    try
    {
      // 外部と内部のキャンセルトークンを連携
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
      
      // 大量のアイテムを効率的に処理するためにバッチ処理
      var itemsList = items.ToList(); // 一度だけ列挙
      
      // バッチサイズを決定（最大100アイテムずつ処理）
      const int batchSize = 100;
      var tasks = new List<Task>();
      
      for (int i = 0; i < itemsList.Count; i += batchSize)
      {
        var batch = itemsList.Skip(i).Take(batchSize);
        var task = ProcessBatchAsync(batch, linkedCts.Token);
        tasks.Add(task);
        
        // 同時実行数を制限（最大5タスク）
        if (tasks.Count >= 5)
        {
          await Task.WhenAny(tasks).ConfigureAwait(false);
          tasks.RemoveAll(t => t.IsCompleted);
        }
      }
      
      // 残りのタスクが完了するのを待つ
      await Task.WhenAll(tasks).ConfigureAwait(false);
      
      return true;
    }
    catch (OperationCanceledException)
    {
      return false;
    }
    catch (ChannelClosedException)
    {
      return false;
    }
  }

  /// <summary>
  /// バッチを処理する内部メソッド
  /// </summary>
  private async Task ProcessBatchAsync(IEnumerable<T> batch, CancellationToken cancellationToken)
  {
    foreach (var item in batch)
    {
      if (cancellationToken.IsCancellationRequested)
        break;
        
      await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
      Interlocked.Increment(ref _count);
    }
  }

  /// <summary>
  /// キューからアイテムを取得する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>キューから取得したアイテム</returns>
  public async Task<T> DequeueAsync(CancellationToken cancellationToken = default)
  {
    if (_disposed) throw new ObjectDisposedException(nameof(BatchQueue<T>));

    try
    {
      // 外部と内部のキャンセルトークンを連携
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
      
      var item = await _channel.Reader.ReadAsync(linkedCts.Token).ConfigureAwait(false);
      Interlocked.Decrement(ref _count);
      return item;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (ChannelClosedException)
    {
      throw;
    }
  }

  /// <summary>
  /// 指定された数のアイテムをキューから取得する
  /// </summary>
  /// <param name="count">取得するアイテムの数</param>
  /// <param name="timeout">タイムアウト（ミリ秒）</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>キューから取得したアイテムのリスト</returns>
  public async Task<List<T>> DequeueMultipleAsync(int count, int timeout, CancellationToken cancellationToken = default)
  {
    if (_disposed) throw new ObjectDisposedException(nameof(BatchQueue<T>));
    
    var result = new List<T>();
    var timeoutToken = new CancellationTokenSource(timeout);
    
    // 3つのキャンセルトークンを連携（内部、タイムアウト、外部）
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutToken.Token, cancellationToken);

    try
    {
      for (int i = 0; i < count; i++)
      {
        if (linkedCts.Token.IsCancellationRequested)
          break;
          
        if (_channel.Reader.TryRead(out var item))
        {
          result.Add(item);
          Interlocked.Decrement(ref _count);
        }
        else
        {
          try
          {
            if (await _channel.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
            {
              if (_channel.Reader.TryRead(out item))
              {
                result.Add(item);
                Interlocked.Decrement(ref _count);
              }
            }
            else
            {
              break;
            }
          }
          catch (OperationCanceledException)
          {
            break;
          }
        }
      }
    }
    finally
    {
      timeoutToken.Dispose();
    }

    return result;
  }

  /// <summary>
  /// キューの内容を非同期ストリームとして取得する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>アイテムの非同期ストリーム</returns>
  public async IAsyncEnumerable<T> GetAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    if (_disposed) throw new ObjectDisposedException(nameof(BatchQueue<T>));
    
    // 外部と内部のキャンセルトークンを連携
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
    
    while (await _channel.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
    {
      while (_channel.Reader.TryRead(out var item))
      {
        Interlocked.Decrement(ref _count);
        yield return item;
        
        if (linkedCts.Token.IsCancellationRequested)
          yield break;
      }
    }
  }

  /// <summary>
  /// キューが空かどうかを確認する
  /// </summary>
  /// <returns>キューが空の場合はtrue、それ以外の場合はfalse</returns>
  public bool IsEmpty => _count == 0;

  /// <summary>
  /// キューに格納されているアイテムの数を取得する
  /// </summary>
  public int Count => _count;

  /// <summary>
  /// キューの容量を取得する
  /// </summary>
  public int Capacity => _capacity;

  /// <summary>
  /// キューを閉じる
  /// </summary>
  public void Close()
  {
    _channel.Writer.Complete();
    _cts.Cancel();
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
    if (_disposed)
      return;
      
    if (disposing)
    {
      Close();
      _cts.Dispose();
    }
    
    _disposed = true;
  }
}