namespace MachineLog.Collector.Services;

/// <summary>
/// IoT Hubサービスのインターフェース
/// </summary>
public interface IIoTHubService : IDisposable, IAsyncDisposable
{
  /// <summary>
  /// IoT Hubに接続します
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>接続結果</returns>
  Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// IoT Hubから切断します
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>タスク</returns>
  Task DisconnectAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// ファイルをIoT Hubにアップロードします
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  /// <param name="blobName">Blobの名前</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>アップロード結果</returns>
  Task<FileUploadResult> UploadFileAsync(string filePath, string blobName, CancellationToken cancellationToken = default);

  /// <summary>
  /// 接続状態を取得します
  /// </summary>
  /// <returns>接続状態</returns>
  ConnectionState GetConnectionState();
}

/// <summary>
/// 接続結果
/// </summary>
public class ConnectionResult
{
  /// <summary>
  /// 接続が成功したかどうか
  /// </summary>
  public bool Success { get; set; }

  /// <summary>
  /// 接続時間（ミリ秒）
  /// </summary>
  public long ConnectionTimeMs { get; set; }

  /// <summary>
  /// エラーメッセージ（エラーがある場合）
  /// </summary>
  public string? ErrorMessage { get; set; }

  /// <summary>
  /// 例外（エラーがある場合）
  /// </summary>
  public Exception? Exception { get; set; }
}

/// <summary>
/// ファイルアップロード結果
/// </summary>
public class FileUploadResult
{
  /// <summary>
  /// アップロードが成功したかどうか
  /// </summary>
  public bool Success { get; set; }

  /// <summary>
  /// アップロードされたファイルのパス
  /// </summary>
  public string FilePath { get; set; } = string.Empty;

  /// <summary>
  /// アップロード先のBlobの名前
  /// </summary>
  public string BlobName { get; set; } = string.Empty;

  /// <summary>
  /// アップロードされたファイルのサイズ（バイト）
  /// </summary>
  public long FileSizeBytes { get; set; }

  /// <summary>
  /// アップロード時間（ミリ秒）
  /// </summary>
  public long UploadTimeMs { get; set; }

  /// <summary>
  /// エラーメッセージ（エラーがある場合）
  /// </summary>
  public string? ErrorMessage { get; set; }

  /// <summary>
  /// 例外（エラーがある場合）
  /// </summary>
  public Exception? Exception { get; set; }
}

/// <summary>
/// 接続状態
/// </summary>
public enum ConnectionState
{
  /// <summary>
  /// 切断
  /// </summary>
  Disconnected,

  /// <summary>
  /// 接続中
  /// </summary>
  Connecting,

  /// <summary>
  /// 接続済み
  /// </summary>
  Connected,

  /// <summary>
  /// 切断中
  /// </summary>
  Disconnecting,

  /// <summary>
  /// エラー
  /// </summary>
  Error
}
