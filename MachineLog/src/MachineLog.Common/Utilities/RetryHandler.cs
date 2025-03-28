using MachineLog.Common.Exceptions;
using MachineLog.Common.Logging;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MachineLog.Common.Utilities;

/// <summary>
/// リトライハンドラー
/// </summary>
public class RetryHandler
{
  private readonly ILogger _logger;
  private readonly StructuredLogger _structuredLogger;
  private readonly ConcurrentDictionary<string, RetryStatistics> _retryStats = new();

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  public RetryHandler(ILogger logger)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _structuredLogger = new StructuredLogger(logger);
  }

  /// <summary>
  /// 非同期操作をリトライ付きで実行します
  /// </summary>
  /// <typeparam name="TResult">戻り値の型</typeparam>
  /// <param name="operationName">操作名</param>
  /// <param name="operation">実行する操作</param>
  /// <param name="retryPolicy">リトライポリシー</param>
  /// <param name="context">リトライコンテキスト</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>操作の結果</returns>
  public async Task<TResult> ExecuteWithRetryAsync<TResult>(
      string operationName,
      Func<CancellationToken, Task<TResult>> operation,
      AsyncRetryPolicy retryPolicy,
      Dictionary<string, object>? context = null,
      CancellationToken cancellationToken = default)
  {
    var retryContext = CreateRetryContext(operationName, context);
    var retryStats = GetOrCreateRetryStatistics(operationName);
    var stopwatch = Stopwatch.StartNew();

    try
    {
      // 操作の実行前にカウンターを更新
      retryStats.AttemptedCount++;

      // リトライポリシーを使用して操作を実行
      var result = await retryPolicy.ExecuteAsync(async (ct) =>
      {
        try
        {
          return await operation(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRetryableAndNotCanceled(ex, ct))
        {
          // リトライ可能な例外をそのまま伝播して、ポリシーに処理させる
          _logger.LogWarning(ex, "リトライ可能なエラーが発生しました: {OperationName}", operationName);
          throw;
        }
        catch (Exception ex) when (!RetryPolicy.IsRetryable(ex) && !ct.IsCancellationRequested)
        {
          // リトライ不可能な例外は詳細にログ記録
          var errorContext = new Dictionary<string, object>(retryContext)
          {
            ["ElapsedMs"] = stopwatch.ElapsedMilliseconds
          };
          _structuredLogger.LogError(ex, operationName, errorContext);
          retryStats.FailedCount++;
          throw;
        }
      }, cancellationToken).ConfigureAwait(false);

      // 成功した場合
      stopwatch.Stop();
      retryStats.SuccessCount++;
      retryStats.LastSuccess = DateTime.UtcNow;

      if (_logger.IsEnabled(LogLevel.Debug))
      {
        _logger.LogDebug("操作が成功しました: {OperationName} ({ElapsedMs}ms)",
            operationName, stopwatch.ElapsedMilliseconds);
      }

      return result;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      stopwatch.Stop();
      retryStats.CancelledCount++;
      _logger.LogInformation("操作がキャンセルされました: {OperationName} ({ElapsedMs}ms)",
          operationName, stopwatch.ElapsedMilliseconds);
      throw; // キャンセルは上位に伝播
    }
    catch (Exception ex)
    {
      // 全てのリトライが失敗した場合
      stopwatch.Stop();
      retryStats.FailedCount++;
      retryStats.LastFailure = DateTime.UtcNow;

      // エラーログを記録
      var errorContext = new Dictionary<string, object>(retryContext)
      {
        ["ElapsedMs"] = stopwatch.ElapsedMilliseconds,
        ["AllRetriesExhausted"] = true
      };
      _structuredLogger.LogError(ex, operationName, errorContext);

      // MachineLogExceptionの場合はそのまま再スロー
      if (ex is MachineLogException)
      {
        throw;
      }

      // 通常の例外をMachineLogExceptionにラップして再スロー
      throw new MachineLogException(
          "RETRY_FAILED",
          ErrorCategory.ExternalService,
          $"操作 {operationName} がリトライ後も失敗しました: {ex.Message}",
          ex,
          false); // リトライはもう試したので、これ以上リトライしない
    }
  }

  /// <summary>
  /// 非同期操作をリトライ付きで実行します（結果なし）
  /// </summary>
  /// <param name="operationName">操作名</param>
  /// <param name="operation">実行する操作</param>
  /// <param name="retryPolicy">リトライポリシー</param>
  /// <param name="context">リトライコンテキスト</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task ExecuteWithRetryAsync(
      string operationName,
      Func<CancellationToken, Task> operation,
      AsyncRetryPolicy retryPolicy,
      Dictionary<string, object>? context = null,
      CancellationToken cancellationToken = default)
  {
    // 結果がないバージョンの操作をラップして、結果ありのメソッドを呼び出す
    await ExecuteWithRetryAsync(
        operationName,
        async (ct) =>
        {
          await operation(ct).ConfigureAwait(false);
          return true;
        },
        retryPolicy,
        context,
        cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// リトライの統計情報をリセットします
  /// </summary>
  public void ResetRetryStatistics()
  {
    _retryStats.Clear();
  }

  /// <summary>
  /// 指定した操作のリトライ統計情報を取得します
  /// </summary>
  /// <param name="operationName">操作名</param>
  /// <returns>リトライ統計情報</returns>
  public RetryStatistics GetRetryStatistics(string operationName)
  {
    return GetOrCreateRetryStatistics(operationName);
  }

  /// <summary>
  /// リトライコンテキストを作成します
  /// </summary>
  /// <param name="operationName">操作名</param>
  /// <param name="additionalContext">追加のコンテキスト情報</param>
  /// <returns>リトライコンテキスト</returns>
  private static Dictionary<string, object> CreateRetryContext(
      string operationName,
      Dictionary<string, object>? additionalContext)
  {
    var context = new Dictionary<string, object>
    {
      ["OperationName"] = operationName,
      ["StartTime"] = DateTime.UtcNow
    };

    // 追加のコンテキスト情報がある場合は統合
    if (additionalContext != null)
    {
      foreach (var entry in additionalContext)
      {
        context[entry.Key] = entry.Value;
      }
    }

    return context;
  }

  /// <summary>
  /// リトライ統計情報を取得または作成します
  /// </summary>
  /// <param name="operationName">操作名</param>
  /// <returns>リトライ統計情報</returns>
  private RetryStatistics GetOrCreateRetryStatistics(string operationName)
  {
    return _retryStats.GetOrAdd(operationName, _ => new RetryStatistics { OperationName = operationName });
  }

  /// <summary>
  /// リトライ可能で、かつキャンセルされていない例外かどうかを判断します
  /// </summary>
  /// <param name="ex">例外</param>
  /// <param name="ct">キャンセレーショントークン</param>
  /// <returns>リトライ可能でキャンセルされていない場合はtrue</returns>
  private static bool IsRetryableAndNotCanceled(Exception ex, CancellationToken ct)
  {
    return RetryPolicy.IsRetryable(ex) && !ct.IsCancellationRequested;
  }
}

/// <summary>
/// リトライの統計情報
/// </summary>
public class RetryStatistics
{
  /// <summary>操作名</summary>
  public string OperationName { get; set; } = string.Empty;

  /// <summary>試行回数</summary>
  public int AttemptedCount { get; set; }

  /// <summary>成功回数</summary>
  public int SuccessCount { get; set; }

  /// <summary>失敗回数</summary>
  public int FailedCount { get; set; }

  /// <summary>キャンセル回数</summary>
  public int CancelledCount { get; set; }

  /// <summary>最後の成功時刻</summary>
  public DateTime? LastSuccess { get; set; }

  /// <summary>最後の失敗時刻</summary>
  public DateTime? LastFailure { get; set; }
}
