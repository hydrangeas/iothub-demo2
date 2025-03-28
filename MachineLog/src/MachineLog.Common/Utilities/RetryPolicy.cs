using MachineLog.Common.Exceptions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net.Sockets;
using System.Text;

namespace MachineLog.Common.Utilities;

/// <summary>
/// リトライポリシーを提供するユーティリティクラス
/// </summary>
public static class RetryPolicy
{
  /// <summary>
  /// 一般的なリトライ可能な例外のポリシーを作成します
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="operationName">操作名</param>
  /// <param name="maxRetryCount">最大リトライ回数</param>
  /// <param name="initialBackoffSeconds">初期バックオフ秒数</param>
  /// <returns>非同期リトライポリシー</returns>
  public static AsyncRetryPolicy CreateDefaultRetryPolicy(
      ILogger logger,
      string operationName,
      int maxRetryCount = 3,
      double initialBackoffSeconds = 1.0)
  {
    return Policy
        .Handle<TimeoutException>()
        .Or<SocketException>()
        .Or<IOException>()
        .Or<InvalidOperationException>(ex => IsRetryable(ex))
        .Or<MachineLogException>(ex => ex.IsRetryable)
        .WaitAndRetryAsync(
            maxRetryCount,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1) * initialBackoffSeconds),
            (exception, timeSpan, retryCount, context) =>
            {
              logger.LogWarning(exception,
                  "{OperationName}: 一時的なエラーが発生しました。{RetryCount}回目のリトライを{RetryTimeSpan:0.00}秒後に実行します。",
                  operationName, retryCount, timeSpan.TotalSeconds);
            });
  }

  /// <summary>
  /// IoT Hub操作用のリトライポリシーを作成します
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="operationName">操作名</param>
  /// <param name="maxRetryCount">最大リトライ回数</param>
  /// <param name="initialBackoffSeconds">初期バックオフ秒数</param>
  /// <returns>非同期リトライポリシー</returns>
  public static AsyncRetryPolicy CreateIoTHubRetryPolicy(
      ILogger logger,
      string operationName,
      int maxRetryCount = 5,
      double initialBackoffSeconds = 1.0)
  {
    return Policy
        .Handle<Microsoft.Azure.Devices.Client.Exceptions.IotHubException>()
        .Or<TimeoutException>()
        .Or<SocketException>()
        .Or<IOException>()
        .Or<MachineLogException>(ex => ex.IsRetryable && ex.Category == ErrorCategory.ExternalService)
        .WaitAndRetryAsync(
            maxRetryCount,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1) * initialBackoffSeconds),
            (exception, timeSpan, retryCount, context) =>
            {
              logger.LogWarning(exception,
                  "{OperationName}: IoT Hub操作中にエラーが発生しました。{RetryCount}回目のリトライを{RetryTimeSpan:0.00}秒後に実行します。",
                  operationName, retryCount, timeSpan.TotalSeconds);
            });
  }

  /// <summary>
  /// HTTP操作用のリトライポリシーを作成します
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="operationName">操作名</param>
  /// <param name="maxRetryCount">最大リトライ回数</param>
  /// <param name="initialBackoffSeconds">初期バックオフ秒数</param>
  /// <returns>非同期リトライポリシー</returns>
  public static AsyncRetryPolicy CreateHttpRetryPolicy(
      ILogger logger,
      string operationName,
      int maxRetryCount = 3,
      double initialBackoffSeconds = 0.5)
  {
    return Policy
        .Handle<HttpRequestException>(ex => ex.StatusCode is System.Net.HttpStatusCode.RequestTimeout
                                         or System.Net.HttpStatusCode.ServiceUnavailable
                                         or System.Net.HttpStatusCode.GatewayTimeout
                                         or System.Net.HttpStatusCode.TooManyRequests)
        .Or<TimeoutException>()
        .Or<SocketException>()
        .Or<IOException>()
        .Or<MachineLogException>(ex => ex.IsRetryable && ex.Category == ErrorCategory.HttpRequest)
        .WaitAndRetryAsync(
            maxRetryCount,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1) * initialBackoffSeconds), // 指数バックオフ
            (exception, timeSpan, retryCount, context) =>
            {
              logger.LogWarning(exception,
                  "{OperationName}: HTTP通信中にエラーが発生しました。{RetryCount}回目のリトライを{RetryTimeSpan:0.00}秒後に実行します。",
                  operationName, retryCount, timeSpan.TotalSeconds);
            });
  }

  /// <summary>
  /// DB操作用のリトライポリシーを作成します
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="operationName">操作名</param>
  /// <param name="maxRetryCount">最大リトライ回数</param>
  /// <param name="initialBackoffSeconds">初期バックオフ秒数</param>
  /// <returns>非同期リトライポリシー</returns>
  public static AsyncRetryPolicy CreateDatabaseRetryPolicy(
      ILogger logger,
      string operationName,
      int maxRetryCount = 3,
      double initialBackoffSeconds = 0.3)
  {
    return Policy
        .Handle<System.Data.Common.DbException>(IsSqlTransientError)
        .Or<TimeoutException>()
        .Or<MachineLogException>(ex => ex.IsRetryable && ex.Category == ErrorCategory.Database)
        .WaitAndRetryAsync(
            maxRetryCount,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(1.5, retryAttempt - 1) * initialBackoffSeconds), // 1.5の累乗でバックオフ
            (exception, timeSpan, retryCount, context) =>
            {
              logger.LogWarning(exception,
                  "{OperationName}: データベース操作中に一時的なエラーが発生しました。{RetryCount}回目のリトライを{RetryTimeSpan:0.00}秒後に実行します。",
                  operationName, retryCount, timeSpan.TotalSeconds);
            });
  }

  /// <summary>
  /// 指定された例外がリトライ可能かどうかを判断します
  /// </summary>
  /// <param name="ex">例外</param>
  /// <returns>リトライ可能かどうか</returns>
  public static bool IsRetryable(Exception ex)
  {
    if (ex is MachineLogException mlException)
    {
      return mlException.IsRetryable;
    }

    // 標準的な一時的エラー
    if (ex is TimeoutException or SocketException or IOException)
    {
      return true;
    }

    // HTTPリクエストの一時的エラー
    if (ex is HttpRequestException httpEx)
    {
      return httpEx.StatusCode is System.Net.HttpStatusCode.RequestTimeout
          or System.Net.HttpStatusCode.ServiceUnavailable
          or System.Net.HttpStatusCode.GatewayTimeout
          or System.Net.HttpStatusCode.TooManyRequests;
    }

    // データベースの一時的エラー
    if (ex is System.Data.Common.DbException dbEx)
    {
      return IsSqlTransientError(dbEx);
    }

    // 一般的に再試行可能なエラーメッセージ
    var message = ex.Message.ToLowerInvariant();
    return message.Contains("timeout") ||
           message.Contains("一時的") ||
           message.Contains("temporary") ||
           message.Contains("transient") ||
           message.Contains("retry");
  }

  /// <summary>
  /// SQLの一時的なエラーかどうかを判断します
  /// </summary>
  /// <param name="ex">データベース例外</param>
  /// <returns>一時的なエラーかどうか</returns>
  private static bool IsSqlTransientError(System.Data.Common.DbException ex)
  {
    // SQL Serverの一時的なエラーコード
    var transientSqlErrorNumbers = new[] {
      -2, // タイムアウト
      4060, // データベースにアクセスできません
      40197, // サービスエラー
      40501, // サービスがビジー状態
      40613, // データベースが現在利用できません
      49918, // リソース不足
      49919, // サービスレベル目標を超えました
      49920, // サービス制限
    };

    // SQL Server例外の場合
    if (ex.GetType().Name == "SqlException" && ex.GetType().Namespace == "Microsoft.Data.SqlClient")
    {
      var errorCode = GetSqlExceptionErrorCode(ex);
      return transientSqlErrorNumbers.Contains(errorCode);
    }

    // Cosmos DB例外の場合
    if (ex.GetType().Name.Contains("CosmosException"))
    {
      var statusCode = GetCosmosExceptionStatusCode(ex);
      // HTTP 429 (TooManyRequests), 503 (ServiceUnavailable), 408 (RequestTimeout)
      return statusCode == 429 || statusCode == 503 || statusCode == 408;
    }

    // メッセージの内容で判断
    var message = ex.Message.ToLowerInvariant();
    return message.Contains("timeout") ||
           message.Contains("connection") && (message.Contains("lost") || message.Contains("closed")) ||
           message.Contains("deadlock") ||
           message.Contains("throttled") ||
           message.Contains("busy") ||
           message.Contains("transient");
  }

  /// <summary>
  /// SQLExceptionからエラーコードを取得します
  /// </summary>
  /// <param name="ex">例外</param>
  /// <returns>エラーコード</returns>
  private static int GetSqlExceptionErrorCode(System.Data.Common.DbException ex)
  {
    try
    {
      // リフレクションを使用してNumberプロパティを取得
      var numberProperty = ex.GetType().GetProperty("Number");
      if (numberProperty != null)
      {
        var value = numberProperty.GetValue(ex);
        if (value != null)
        {
          return Convert.ToInt32(value);
        }
      }
    }
    catch
    {
      // リフレクションエラーは無視
    }
    return 0;
  }

  /// <summary>
  /// CosmosExceptionからStatusCodeを取得します
  /// </summary>
  /// <param name="ex">例外</param>
  /// <returns>ステータスコード</returns>
  private static int GetCosmosExceptionStatusCode(System.Data.Common.DbException ex)
  {
    try
    {
      // リフレクションを使用してStatusCodeプロパティを取得
      var statusCodeProperty = ex.GetType().GetProperty("StatusCode");
      if (statusCodeProperty != null)
      {
        var value = statusCodeProperty.GetValue(ex);
        if (value != null)
        {
          return Convert.ToInt32(value);
        }
      }
    }
    catch
    {
      // リフレクションエラーは無視
    }
    return 0;
  }
}
