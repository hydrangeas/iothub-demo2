using System.Runtime.Serialization;

namespace MachineLog.Common.Exceptions;

/// <summary>
/// MachineLogの基本例外クラス
/// </summary>
[Serializable]
public class MachineLogException : Exception
{
  /// <summary>
  /// エラーコード
  /// </summary>
  public string ErrorCode { get; }

  /// <summary>
  /// エラーカテゴリ
  /// </summary>
  public ErrorCategory Category { get; }

  /// <summary>
  /// 追加のコンテキスト情報
  /// </summary>
  public IDictionary<string, object> Context { get; }

  /// <summary>
  /// リトライ可能なエラーかどうか
  /// </summary>
  public bool IsRetryable { get; }

  /// <summary>
  /// 最大リトライ回数（リトライ不可の場合は0）
  /// </summary>
  public int MaxRetryCount { get; }

  /// <summary>
  /// タイムスタンプ
  /// </summary>
  public DateTime Timestamp { get; }

  /// <summary>
  /// コンストラクタ
  /// </summary>
  public MachineLogException() : this("GENERAL_ERROR", ErrorCategory.General, "不明なエラーが発生しました", null) { }

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="message">エラーメッセージ</param>
  public MachineLogException(string message) : this("GENERAL_ERROR", ErrorCategory.General, message, null) { }

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="message">エラーメッセージ</param>
  /// <param name="innerException">内部例外</param>
  public MachineLogException(string message, Exception innerException)
      : this("GENERAL_ERROR", ErrorCategory.General, message, innerException) { }

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="errorCode">エラーコード</param>
  /// <param name="category">エラーカテゴリ</param>
  /// <param name="message">エラーメッセージ</param>
  /// <param name="innerException">内部例外</param>
  /// <param name="isRetryable">リトライ可能かどうか</param>
  /// <param name="maxRetryCount">最大リトライ回数</param>
  /// <param name="context">追加のコンテキスト情報</param>
  public MachineLogException(
      string errorCode,
      ErrorCategory category,
      string message,
      Exception? innerException = null,
      bool isRetryable = false,
      int maxRetryCount = 0,
      IDictionary<string, object>? context = null)
      : base(message, innerException)
  {
    ErrorCode = errorCode;
    Category = category;
    IsRetryable = isRetryable;
    MaxRetryCount = maxRetryCount;
    Context = context ?? new Dictionary<string, object>();
    Timestamp = DateTime.UtcNow;
  }

  /// <summary>
  /// シリアル化用コンストラクタ
  /// </summary>
  /// <param name="info">シリアル化情報</param>
  /// <param name="context">ストリーミングコンテキスト</param>
  protected MachineLogException(SerializationInfo info, StreamingContext context)
      : base(info, context)
  {
    ErrorCode = info.GetString(nameof(ErrorCode)) ?? "UNKNOWN_ERROR";
    Category = (ErrorCategory)info.GetValue(nameof(Category), typeof(ErrorCategory));
    IsRetryable = info.GetBoolean(nameof(IsRetryable));
    MaxRetryCount = info.GetInt32(nameof(MaxRetryCount));
    Context = (IDictionary<string, object>)info.GetValue(nameof(Context), typeof(IDictionary<string, object>));
    Timestamp = info.GetDateTime(nameof(Timestamp));
  }

  /// <summary>
  /// シリアル化処理
  /// </summary>
  /// <param name="info">シリアル化情報</param>
  /// <param name="context">ストリーミングコンテキスト</param>
  public override void GetObjectData(SerializationInfo info, StreamingContext context)
  {
    if (info == null)
    {
      throw new ArgumentNullException(nameof(info));
    }

    info.AddValue(nameof(ErrorCode), ErrorCode);
    info.AddValue(nameof(Category), Category);
    info.AddValue(nameof(IsRetryable), IsRetryable);
    info.AddValue(nameof(MaxRetryCount), MaxRetryCount);
    info.AddValue(nameof(Context), Context);
    info.AddValue(nameof(Timestamp), Timestamp);

    base.GetObjectData(info, context);
  }

  /// <summary>
  /// 例外情報を構造化した文字列で取得します
  /// </summary>
  /// <returns>構造化された例外情報</returns>
  public override string ToString()
  {
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"[{ErrorCode}] ({Category}) {Message}");
    sb.AppendLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
    sb.AppendLine($"Retryable: {IsRetryable} (MaxRetries: {MaxRetryCount})");

    if (Context.Count > 0)
    {
      sb.AppendLine("Context:");
      foreach (var kvp in Context)
      {
        sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
      }
    }

    if (InnerException != null)
    {
      sb.AppendLine($"InnerException: {InnerException.Message}");
      sb.AppendLine(InnerException.StackTrace);
    }

    sb.AppendLine(StackTrace);

    return sb.ToString();
  }
}

/// <summary>
/// エラーカテゴリ
/// </summary>
public enum ErrorCategory
{
  /// <summary>一般的なエラー</summary>
  General,
  /// <summary>入力検証エラー</summary>
  Validation,
  /// <summary>ネットワークエラー</summary>
  Network,
  /// <summary>HTTP通信エラー</summary>
  HttpRequest,
  /// <summary>データベースエラー</summary>
  Database,
  /// <summary>ストレージエラー</summary>
  Storage,
  /// <summary>セキュリティエラー</summary>
  Security,
  /// <summary>設定エラー</summary>
  Configuration,
  /// <summary>リソースエラー</summary>
  Resource,
  /// <summary>外部サービスエラー</summary>
  ExternalService,
  /// <summary>IoTデバイスエラー</summary>
  IoTDevice,
  /// <summary>ファイル処理エラー</summary>
  FileProcessing,
  /// <summary>システムエラー</summary>
  System
}
