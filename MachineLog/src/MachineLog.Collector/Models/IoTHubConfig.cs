namespace MachineLog.Collector.Models;

/// <summary>
/// IoT Hubの設定を定義するクラス
/// </summary>
public class IoTHubConfig
{
  /// <summary>
  /// IoT Hub接続文字列
  /// </summary>
  public string ConnectionString { get; set; } = string.Empty;

  /// <summary>
  /// デバイスID
  /// </summary>
  public string DeviceId { get; set; } = string.Empty;

  /// <summary>
  /// SASトークン
  /// </summary>
  public string? SasToken { get; set; }

  /// <summary>
  /// アップロード先フォルダパス
  /// </summary>
  public string UploadFolderPath { get; set; } = "logs";

  /// <summary>
  /// ファイルアップロード設定
  /// </summary>
  public FileUploadConfig FileUpload { get; set; } = new();
}

/// <summary>
/// ファイルアップロード設定を定義するクラス
/// </summary>
public class FileUploadConfig
{
  /// <summary>
  /// SASトークンの有効期間（分）
  /// </summary>
  public int SasTokenTimeToLiveMinutes { get; set; } = 60;

  /// <summary>
  /// アップロード通知を有効にするかどうか
  /// </summary>
  public bool EnableNotification { get; set; } = true;

  /// <summary>
  /// アップロードのロック時間（分）
  /// </summary>
  public int LockDurationMinutes { get; set; } = 1;

  /// <summary>
  /// アップロードファイルのデフォルト保持期間（日）
  /// </summary>
  public int DefaultTimeToLiveDays { get; set; } = 1;

  /// <summary>
  /// 最大配信試行回数
  /// </summary>
  public int MaxDeliveryCount { get; set; } = 10;
}
