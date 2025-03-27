using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MachineLog.Common.Models;

namespace MachineLog.Common.Batch;

/// <summary>
/// LogEntryのバッチ処理を行うクラス
/// </summary>
public class LogEntryBatchProcessor : BatchProcessorBase<LogEntry>
{
  private readonly Func<List<LogEntry>, Task<bool>> _processBatchFunc;

  /// <summary>
  /// LogEntryのバッチ処理を行うクラスを初期化する
  /// </summary>
  /// <param name="processBatchFunc">バッチを処理する関数</param>
  /// <param name="options">バッチ処理のオプション</param>
  public LogEntryBatchProcessor(
      Func<List<LogEntry>, Task<bool>> processBatchFunc,
      BatchProcessorOptions options = null)
      : base(options ?? BatchProcessorOptions.Default)
  {
    _processBatchFunc = processBatchFunc ?? throw new ArgumentNullException(nameof(processBatchFunc));
  }

  /// <summary>
  /// バッチのサイズを計算する
  /// </summary>
  /// <param name="batch">サイズを計算するバッチ</param>
  /// <returns>バッチのサイズ（バイト単位）</returns>
  protected override int CalculateBatchSize(List<LogEntry> batch)
  {
    if (batch == null || batch.Count == 0)
      return 0;

    int totalSize = 0;

    foreach (var entry in batch)
    {
      // JSONシリアライズしてサイズを計算
      var json = JsonSerializer.Serialize(entry);
      totalSize += Encoding.UTF8.GetByteCount(json);
    }

    return totalSize;
  }

  /// <summary>
  /// バッチを処理する
  /// </summary>
  /// <param name="batch">処理するバッチ</param>
  /// <returns>処理が成功したかどうかを示す非同期タスク</returns>
  protected override async Task<bool> ProcessBatchItemsAsync(List<LogEntry> batch)
  {
    if (batch == null || batch.Count == 0)
      return true;

    try
    {
      // 処理時間を設定
      var processedAt = DateTime.UtcNow;
      foreach (var entry in batch)
      {
        entry.ProcessedAt = processedAt;
      }

      // バッチ処理関数を呼び出す
      return await _processBatchFunc(batch);
    }
    catch (Exception ex)
    {
      // エラーログ出力など
      Console.Error.WriteLine($"バッチ処理でエラーが発生しました: {ex}");
      return false;
    }
  }
}
