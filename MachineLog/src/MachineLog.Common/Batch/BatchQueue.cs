using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MachineLog.Common.Batch;

/// <summary>
/// バッチキューを表すクラス
/// </summary>
/// <typeparam name="T">キューに格納する型</typeparam>
public class BatchQueue<T> where T : class
{
  private readonly Channel<T> _channel;
  private readonly int _capacity;
  private readonly CancellationTokenSource _cts;
  private int _count;

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
      SingleWriter = false
    };

    _channel = Channel.CreateBounded<T>(options);
  }

  /// <summary>
  /// アイテムをキューに追加する
  /// </summary>
  /// <param name="item">追加するアイテム</param>
  /// <returns>追加が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> EnqueueAsync(T item)
  {
    try
    {
      await _channel.Writer.WriteAsync(item, _cts.Token);
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
  /// <returns>追加が成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> EnqueueRangeAsync(IEnumerable<T> items)
  {
    try
    {
      foreach (var item in items)
      {
        await _channel.Writer.WriteAsync(item, _cts.Token);
        Interlocked.Increment(ref _count);
      }
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
  /// キューからアイテムを取得する
  /// </summary>
  /// <returns>キューから取得したアイテム</returns>
  public async Task<T> DequeueAsync()
  {
    try
    {
      var item = await _channel.Reader.ReadAsync(_cts.Token);
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
  /// <returns>キューから取得したアイテムのリスト</returns>
  public async Task<List<T>> DequeueMultipleAsync(int count, int timeout)
  {
    var result = new List<T>();
    var timeoutToken = new CancellationTokenSource(timeout);
    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutToken.Token);

    try
    {
      for (int i = 0; i < count; i++)
      {
        if (_channel.Reader.TryRead(out var item))
        {
          result.Add(item);
          Interlocked.Decrement(ref _count);
        }
        else
        {
          try
          {
            if (await _channel.Reader.WaitToReadAsync(linkedCts.Token))
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
      linkedCts.Dispose();
    }

    return result;
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
    _cts.Dispose();
  }
}