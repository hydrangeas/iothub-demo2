using System.Text.Json.Serialization;

namespace MachineLog.Common.Models;

/// <summary>
/// ログエントリを表すクラス
/// </summary>
public class LogEntry
{
  /// <summary>
  /// ログID
  /// </summary>
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  /// <summary>
  /// タイムスタンプ
  /// </summary>
  [JsonPropertyName("timestamp")]
  public DateTime Timestamp { get; set; }

  /// <summary>
  /// デバイスID
  /// </summary>
  [JsonPropertyName("deviceId")]
  public string DeviceId { get; set; } = string.Empty;

  /// <summary>
  /// ログレベル
  /// </summary>
  [JsonPropertyName("level")]
  public string Level { get; set; } = string.Empty;

  /// <summary>
  /// メッセージ
  /// </summary>
  [JsonPropertyName("message")]
  public string Message { get; set; } = string.Empty;

  /// <summary>
  /// カテゴリ
  /// </summary>
  [JsonPropertyName("category")]
  public string? Category { get; set; }

  /// <summary>
  /// タグ
  /// </summary>
  [JsonPropertyName("tags")]
  public List<string>? Tags { get; set; }

  /// <summary>
  /// 追加データ
  /// </summary>
  [JsonPropertyName("data")]
  public Dictionary<string, object>? Data { get; set; }

  /// <summary>
  /// エラー情報
  /// </summary>
  [JsonPropertyName("error")]
  public ErrorInfo? Error { get; set; }

  /// <summary>
  /// ソースファイル
  /// </summary>
  [JsonPropertyName("sourceFile")]
  public string? SourceFile { get; set; }

  /// <summary>
  /// 処理日時
  /// </summary>
  [JsonPropertyName("processedAt")]
  public DateTime? ProcessedAt { get; set; }
}

/// <summary>
/// エラー情報を表すクラス
/// </summary>
public class ErrorInfo
{
  /// <summary>
  /// エラーコード
  /// </summary>
  [JsonPropertyName("code")]
  public string? Code { get; set; }

  /// <summary>
  /// エラーメッセージ
  /// </summary>
  [JsonPropertyName("message")]
  public string? Message { get; set; }

  /// <summary>
  /// スタックトレース
  /// </summary>
  [JsonPropertyName("stackTrace")]
  public string? StackTrace { get; set; }
}