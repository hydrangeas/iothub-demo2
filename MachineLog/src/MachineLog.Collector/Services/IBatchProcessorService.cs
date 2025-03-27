using MachineLog.Common.Models;

namespace MachineLog.Collector.Services;

/// <summary>
/// バッチ処理サービスのインターフェース
/// </summary>
public interface IBatchProcessorService : IDisposable, IAsyncDisposable
{
  /// <summary>
  /// エントリをバッチに追加します
  /// </summary>
  /// <param name="entry">ログエントリ</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>追加が成功したかどうか</returns>
  Task<bool> AddToBatchAsync(LogEntry entry, CancellationToken cancellationToken = default);

  /// <summary>
  /// 複数のエントリをバッチに追加します
  /// </summary>
  /// <param name="entries">ログエントリのコレクション</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>追加が成功したかどうか</returns>
  Task<bool> AddRangeToBatchAsync(IEnumerable<LogEntry> entries, CancellationToken cancellationToken = default);

  /// <summary>
  /// 現在のバッチを処理します
  /// </summary>
  /// <param name="force">強制的に処理するかどうか（バッチサイズやカウントに関わらず）</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>処理結果</returns>
  Task<BatchProcessingResult> ProcessBatchAsync(bool force = false, CancellationToken cancellationToken = default);

  /// <summary>
  /// バッチ処理を開始します
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>タスク</returns>
  Task StartAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// バッチ処理を停止します
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>タスク</returns>
  Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// バッチ処理結果
/// </summary>
public class BatchProcessingResult
{
  /// <summary>
  /// 処理が成功したかどうか
  /// </summary>
  public bool Success { get; set; }

  /// <summary>
  /// 処理されたアイテム数
  /// </summary>
  public int ProcessedItems { get; set; }

  /// <summary>
  /// バッチサイズ（バイト）
  /// </summary>
  public long BatchSizeBytes { get; set; }

  /// <summary>
  /// 処理時間（ミリ秒）
  /// </summary>
  public long ProcessingTimeMs { get; set; }

  /// <summary>
  /// エラーメッセージ（エラーがある場合）
  /// </summary>
  public string? ErrorMessage { get; set; }

  /// <summary>
  /// 例外（エラーがある場合）
  /// </summary>
  public Exception? Exception { get; set; }
}
