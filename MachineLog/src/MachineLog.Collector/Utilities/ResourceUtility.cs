using Microsoft.Extensions.Logging;

namespace MachineLog.Collector.Utilities;

/// <summary>
/// リソース管理のユーティリティクラス
/// </summary>
public static class ResourceUtility
{
    /// <summary>
    /// リソースを安全に解放します
    /// </summary>
    /// <typeparam name="T">ロガーの型</typeparam>
    /// <param name="logger">ロガー</param>
    /// <param name="resource">解放するリソース</param>
    /// <param name="resourceName">リソース名（ログ出力用）</param>
    /// <returns>解放に成功したかどうか</returns>
    public static bool SafeDispose<T>(ILogger<T> logger, IDisposable? resource, string resourceName)
    {
        if (resource == null)
        {
            return true;
        }

        try
        {
            resource.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{ResourceName}の解放中にエラーが発生しました", resourceName);
            return false;
        }
    }

    /// <summary>
    /// リソースを安全に非同期で解放します
    /// </summary>
    /// <typeparam name="T">ロガーの型</typeparam>
    /// <param name="logger">ロガー</param>
    /// <param name="resource">解放するリソース</param>
    /// <param name="resourceName">リソース名（ログ出力用）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>解放に成功したかどうかを示すタスク</returns>
    public static async Task<bool> SafeDisposeAsync<T>(
        ILogger<T> logger, 
        IAsyncDisposable? resource, 
        string resourceName,
        CancellationToken cancellationToken = default)
    {
        if (resource == null)
        {
            return true;
        }

        try
        {
            await resource.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("{ResourceName}の非同期解放がキャンセルされました", resourceName);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{ResourceName}の非同期解放中にエラーが発生しました", resourceName);
            return false;
        }
    }

    /// <summary>
    /// タイムアウト付きで非同期操作を実行します
    /// </summary>
    /// <typeparam name="T">ロガーの型</typeparam>
    /// <typeparam name="TResult">結果の型</typeparam>
    /// <param name="logger">ロガー</param>
    /// <param name="operationName">操作名（ログ出力用）</param>
    /// <param name="func">実行する非同期関数</param>
    /// <param name="timeoutSeconds">タイムアウト時間（秒）</param>
    /// <param name="defaultValue">タイムアウト時のデフォルト値</param>
    /// <returns>操作の結果、またはタイムアウト時のデフォルト値を含むタスク</returns>
    public static async Task<TResult> ExecuteWithTimeoutAsync<T, TResult>(
        ILogger<T> logger,
        string operationName,
        Func<CancellationToken, Task<TResult>> func,
        int timeoutSeconds,
        TResult defaultValue)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            return await func(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            logger.LogWarning("{OperationName}が{Timeout}秒でタイムアウトしました", operationName, timeoutSeconds);
            return defaultValue;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{OperationName}の実行中にエラーが発生しました", operationName);
            return defaultValue;
        }
    }

    /// <summary>
    /// タイムアウト付きで非同期操作を実行します（結果なし）
    /// </summary>
    /// <typeparam name="T">ロガーの型</typeparam>
    /// <param name="logger">ロガー</param>
    /// <param name="operationName">操作名（ログ出力用）</param>
    /// <param name="func">実行する非同期関数</param>
    /// <param name="timeoutSeconds">タイムアウト時間（秒）</param>
    /// <returns>操作が成功したかどうかを示すタスク</returns>
    public static async Task<bool> ExecuteWithTimeoutAsync<T>(
        ILogger<T> logger,
        string operationName,
        Func<CancellationToken, Task> func,
        int timeoutSeconds)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await func(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            logger.LogWarning("{OperationName}が{Timeout}秒でタイムアウトしました", operationName, timeoutSeconds);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{OperationName}の実行中にエラーが発生しました", operationName);
            return false;
        }
    }
}