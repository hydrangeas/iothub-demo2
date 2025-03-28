using MachineLog.Common.Exceptions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace MachineLog.Common.Logging;

/// <summary>
/// 構造化ログを提供するロガー
/// </summary>
public class StructuredLogger
{
  private readonly ILogger _logger;
  private readonly ConcurrentDictionary<string, int> _errorCounters = new();
  private readonly ConcurrentDictionary<string, DateTime> _lastOccurrence = new();

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  public StructuredLogger(ILogger logger)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
  }

  /// <summary>
  /// エラーを記録します
  /// </summary>
  /// <param name="error">エラー</param>
  /// <param name="operationName">操作名</param>
  /// <param name="additionalContext">追加のコンテキスト情報</param>
  public void LogError(
      Exception error,
      string operationName,
      object? additionalContext = null)
  {
    try
    {
      // エラー発生カウンターを更新
      string errorKey = GetErrorKey(error, operationName);
      int count = _errorCounters.AddOrUpdate(errorKey, 1, (_, c) => c + 1);
      _lastOccurrence[errorKey] = DateTime.UtcNow;

      // コンテキスト情報を構築
      var context = new Dictionary<string, object>
      {
        ["OperationName"] = operationName,
        ["ErrorCount"] = count,
        ["ErrorType"] = error.GetType().Name,
        ["StackTrace"] = error.StackTrace ?? "StackTrace not available"
      };

      // MachineLogExceptionの場合は追加情報を記録
      if (error is MachineLogException mlException)
      {
        context["ErrorCode"] = mlException.ErrorCode;
        context["Category"] = mlException.Category.ToString();
        context["Timestamp"] = mlException.Timestamp;
        context["IsRetryable"] = mlException.IsRetryable;

        // MachineLogException独自のコンテキストを統合
        foreach (var item in mlException.Context)
        {
          context[$"Context_{item.Key}"] = item.Value;
        }
      }

      // 追加のコンテキスト情報がある場合は統合
      if (additionalContext != null)
      {
        var props = additionalContext.GetType().GetProperties();
        foreach (var prop in props)
        {
          object? value = prop.GetValue(additionalContext);
          if (value != null)
          {
            context[$"AdditionalContext_{prop.Name}"] = value;
          }
        }
      }

      // 内部例外がある場合はその情報も記録
      if (error.InnerException != null)
      {
        context["InnerErrorType"] = error.InnerException.GetType().Name;
        context["InnerErrorMessage"] = error.InnerException.Message;
      }

      // 構造化ログとして出力
      _logger.LogError(error, "エラーが発生しました: {ErrorMessage} ({OperationName}, 発生回数: {ErrorCount}回)",
          error.Message, operationName, count);

      // 詳細なコンテキスト情報をJSON形式で記録（Debug用）
      if (_logger.IsEnabled(LogLevel.Debug))
      {
        string contextJson = JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true });
        _logger.LogDebug("エラーコンテキスト: {ContextJson}", contextJson);
      }
    }
    catch (Exception ex)
    {
      // ロギング自体の失敗を防止
      _logger.LogError(ex, "エラーのログ記録中にエラーが発生しました");
    }
  }

  /// <summary>
  /// 警告を記録します
  /// </summary>
  /// <param name="message">警告メッセージ</param>
  /// <param name="operationName">操作名</param>
  /// <param name="additionalContext">追加のコンテキスト情報</param>
  public void LogWarning(
      string message,
      string operationName,
      object? additionalContext = null)
  {
    try
    {
      // コンテキスト情報を構築
      var context = new Dictionary<string, object>
      {
        ["OperationName"] = operationName
      };

      // 追加のコンテキスト情報がある場合は統合
      if (additionalContext != null)
      {
        var props = additionalContext.GetType().GetProperties();
        foreach (var prop in props)
        {
          object? value = prop.GetValue(additionalContext);
          if (value != null)
          {
            context[$"AdditionalContext_{prop.Name}"] = value;
          }
        }
      }

      // 構造化ログとして出力
      _logger.LogWarning("警告: {WarningMessage} ({OperationName})", message, operationName);

      // 詳細なコンテキスト情報をJSON形式で記録（Debug用）
      if (_logger.IsEnabled(LogLevel.Debug) && additionalContext != null)
      {
        string contextJson = JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true });
        _logger.LogDebug("警告コンテキスト: {ContextJson}", contextJson);
      }
    }
    catch (Exception ex)
    {
      // ロギング自体の失敗を防止
      _logger.LogError(ex, "警告のログ記録中にエラーが発生しました");
    }
  }

  /// <summary>
  /// エラーをキーにする一意のエラーキーを生成します
  /// </summary>
  /// <param name="error">エラー</param>
  /// <param name="operationName">操作名</param>
  /// <returns>エラーキー</returns>
  private static string GetErrorKey(Exception error, string operationName)
  {
    if (error is MachineLogException mlException)
    {
      return $"{operationName}_{mlException.ErrorCode}_{mlException.Category}";
    }

    return $"{operationName}_{error.GetType().Name}";
  }

  /// <summary>
  /// エラー発生回数をリセットします
  /// </summary>
  public void ResetErrorCounters()
  {
    _errorCounters.Clear();
    _lastOccurrence.Clear();
  }

  /// <summary>
  /// 特定のエラーの発生回数を取得します
  /// </summary>
  /// <param name="errorKey">エラーキー</param>
  /// <returns>エラー発生回数</returns>
  public int GetErrorCount(string errorKey)
  {
    return _errorCounters.TryGetValue(errorKey, out int count) ? count : 0;
  }

  /// <summary>
  /// 特定のエラーの最終発生時刻を取得します
  /// </summary>
  /// <param name="errorKey">エラーキー</param>
  /// <returns>最終発生時刻、またはnull</returns>
  public DateTime? GetLastOccurrence(string errorKey)
  {
    return _lastOccurrence.TryGetValue(errorKey, out DateTime time) ? time : null;
  }
}
