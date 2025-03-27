using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net.Sockets;

namespace MachineLog.Collector.Utilities;

/// <summary>
/// エラー処理のユーティリティクラス
/// </summary>
public static class ErrorHandlingUtility
{
    /// <summary>
    /// 標準的なリトライポリシーを作成します
    /// </summary>
    /// <typeparam name="T">ロガーの型</typeparam>
    /// <param name="logger">ロガー</param>
    /// <param name="operationName">操作名（ログ出力用）</param>
    /// <param name="maxRetryCount">最大リトライ回数</param>
    /// <param name="initialBackoffSeconds">初期バックオフ時間（秒）</param>
    /// <returns>リトライポリシー</returns>
    public static AsyncRetryPolicy CreateStandardRetryPolicy<T>(
        ILogger<T> logger,
        string operationName,
        int maxRetryCount = 5,
        double initialBackoffSeconds = 1)
    {
        return Policy
            .Handle<IOException>()
            .Or<TimeoutException>()
            .Or<SocketException>()
            .WaitAndRetryAsync(
                maxRetryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt) * initialBackoffSeconds),
                (exception, timeSpan, retryCount, context) =>
                {
                    logger.LogWarning(exception,
                        "{OperationName}中にエラーが発生しました。{RetryCount}回目のリトライを{RetryTimeSpan:0.00}秒後に実行します。",
                        operationName, retryCount, timeSpan.TotalSeconds);
                });
    }

    /// <summary>
    /// 操作を安全に実行します（例外をキャッチしてログに記録）
    /// </summary>
    /// <typeparam name="T">ロガーの型</typeparam>
    /// <param name="logger">ロガー</param>
    /// <param name="operationName">操作名（ログ出力用）</param>
    /// <param name="action">実行するアクション</param>
    /// <param name="logLevel">エラー時のログレベル</param>
    /// <returns>操作が成功したかどうか</returns>
    public static bool SafeExecute<T>(
        ILogger<T> logger,
        string operationName,
        Action action,
        LogLevel logLevel = LogLevel.Error)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            logger.Log(logLevel, ex, "{OperationName}の実行中にエラーが発生しました", operationName);
            return false;
        }
    }

    /// <summary>
    /// 操作を安全に実行し、結果を返します（例外をキャッチしてログに記録）
    /// </summary>
    /// <typeparam name="T">ロガーの型</typeparam>
    /// <typeparam name="TResult">結果の型</typeparam>
    /// <param name="logger">ロガー</param>
    /// <param name="operationName">操作名（ログ出力用）</param>
    /// <param name="func">実行する関数</param>
    /// <param name="defaultValue">エラー時のデフォルト値</param>
    /// <param name="logLevel">エラー時のログレベル</param>
    /// <returns>操作の結果、またはエラー時のデフォルト値</returns>
    public static TResult SafeExecute<T, TResult>(
        ILogger<T> logger,
        string operationName,
        Func<TResult> func,
        TResult defaultValue,
        LogLevel logLevel = LogLevel.Error)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            logger.Log(logLevel, ex, "{OperationName}の実行中にエラーが発生しました", operationName);
            return defaultValue;
        }
    }

    /// <summary>
    /// 非同期操作を安全に実行します（例外をキャッチしてログに記録）
    /// </summary>
    /// <typeparam name="T">ロガーの型</typeparam>
    /// <param name="logger">ロガー</param>
    /// <param name="operationName">操作名（ログ出力用）</param>
    /// <param name="func">実行する非同期関数</param>
    /// <param name="logLevel">エラー時のログレベル</param>
    /// <returns>操作が成功したかどうかを示すタスク</returns>
    public static async Task<bool> SafeExecuteAsync<T>(
        ILogger<T> logger,
        string operationName,
        Func<Task> func,
        LogLevel logLevel = LogLevel.Error)
    {
        try
        {
            await func().ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException ex)
        {
            logger.Log(LogLevel.Information, ex, "{OperationName}がキャンセルされました", operationName);
            return false;
        }
        catch (Exception ex)
        {
            logger.Log(logLevel, ex, "{OperationName}の実行中にエラーが発生しました", operationName);
            return false;
        }
    }

    /// <summary>
    /// 非同期操作を安全に実行し、結果を返します（例外をキャッチしてログに記録）
    /// </summary>
    /// <typeparam name="T">ロガーの型</typeparam>
    /// <typeparam name="TResult">結果の型</typeparam>
    /// <param name="logger">ロガー</param>
    /// <param name="operationName">操作名（ログ出力用）</param>
    /// <param name="func">実行する非同期関数</param>
    /// <param name="defaultValue">エラー時のデフォルト値</param>
    /// <param name="logLevel">エラー時のログレベル</param>
    /// <returns>操作の結果、またはエラー時のデフォルト値を含むタスク</returns>
    public static async Task<TResult> SafeExecuteAsync<T, TResult>(
        ILogger<T> logger,
        string operationName,
        Func<Task<TResult>> func,
        TResult defaultValue,
        LogLevel logLevel = LogLevel.Error)
    {
        try
        {
            return await func().ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            logger.Log(LogLevel.Information, ex, "{OperationName}がキャンセルされました", operationName);
            return defaultValue;
        }
        catch (Exception ex)
        {
            logger.Log(logLevel, ex, "{OperationName}の実行中にエラーが発生しました", operationName);
            return defaultValue;
        }
    }
}