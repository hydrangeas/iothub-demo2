using System.Text;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイル処理サービスのインターフェース
/// </summary>
public interface IFileProcessorService
{
  /// <summary>
  /// ファイルを処理します
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>処理結果</returns>
  Task<FileProcessingResult> ProcessFileAsync(string filePath, CancellationToken cancellationToken = default);

  /// <summary>
  /// ファイルのエンコーディングを検出します
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  /// <returns>検出されたエンコーディング</returns>
  Task<Encoding> DetectEncodingAsync(string filePath);

  /// <summary>
  /// ファイルをフィルタリングします（処理対象かどうかを判断）
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  /// <returns>処理対象の場合はtrue</returns>
  bool ShouldProcessFile(string filePath);
}

/// <summary>
/// ファイル処理結果
/// </summary>
public class FileProcessingResult
{
  /// <summary>
  /// 処理が成功したかどうか
  /// </summary>
  public bool Success { get; set; }

  /// <summary>
  /// 処理されたレコード数
  /// </summary>
  public int ProcessedRecords { get; set; }

  /// <summary>
  /// エラーメッセージ（エラーがある場合）
  /// </summary>
  public string? ErrorMessage { get; set; }

  /// <summary>
  /// 例外（エラーがある場合）
  /// </summary>
  public Exception? Exception { get; set; }

  /// <summary>
  /// 処理時間（ミリ秒）
  /// </summary>
  public long ProcessingTimeMs { get; set; }

  /// <summary>
  /// ファイルサイズ（バイト）
  /// </summary>
  public long FileSizeBytes { get; set; }
}