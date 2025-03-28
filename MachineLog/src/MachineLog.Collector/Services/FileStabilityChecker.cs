using MachineLog.Common.Synchronization;
using MachineLog.Common.Utilities; // 追加
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイルの安定性を監視し、安定したときにイベントを発行するサービス。
/// </summary>
public interface IFileStabilityChecker : IDisposable, IAsyncDisposable
{
  /// <summary>
  /// ファイル安定化イベント
  /// </summary>
  event EventHandler<FileSystemEventArgs>? FileStabilized;

  /// <summary>
  /// 監視対象のファイルを追加または更新します。
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  void TrackFile(string filePath);

  /// <summary>
  /// 安定性チェックを開始します。
  /// </summary>
  /// <param name="stabilizationPeriod">安定期間（秒）</param>
  /// <param name="checkInterval">チェック間隔（ミリ秒）</param>
  /// <param name="cancellationToken">キャンセルトークン</param>
  Task StartAsync(int stabilizationPeriod, int checkInterval, CancellationToken cancellationToken);

  /// <summary>
  /// 安定性チェックを停止します。
  /// </summary>
  /// <param name="cancellationToken">キャンセルトークン</param>
  Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// ファイル安定性チェッカーの実装
/// </summary>
public class FileStabilityChecker : AsyncDisposableBase<FileStabilityChecker>, IFileStabilityChecker
{
  private readonly ILogger<FileStabilityChecker> _logger;
  private readonly ConcurrentDictionary<string, DateTime> _processingFiles = new();
  private readonly AsyncLock _fileOperationLock = new AsyncLock();
  private Timer? _stabilityCheckTimer;
  private int _stabilizationPeriodSeconds;
  private bool _disposed; // Disposeパターン用

  /// <summary>
  /// ファイル安定化イベント
  /// </summary>
  public event EventHandler<FileSystemEventArgs>? FileStabilized;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  public FileStabilityChecker(ILogger<FileStabilityChecker> logger) : base(true) // リソースマネージャーに登録
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
  }

  /// <summary>
  /// 監視対象のファイルを追加または更新します。
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  public void TrackFile(string filePath)
  {
    ThrowIfDisposed(); // オブジェクトが破棄済みの場合は例外をスロー
    if (string.IsNullOrEmpty(filePath))
    {
      _logger.LogWarning("空のファイルパスが指定されました。");
      return;
    }
    _processingFiles[filePath] = DateTime.UtcNow;
    _logger.LogTrace("ファイルを追跡開始/更新: {FilePath}", filePath);
  }

  /// <summary>
  /// 安定性チェックを開始します。
  /// </summary>
  /// <param name="stabilizationPeriod">安定期間（秒）</param>
  /// <param name="checkInterval">チェック間隔（ミリ秒）</param>
  /// <param name="cancellationToken">キャンセルトークン</param>
  public Task StartAsync(int stabilizationPeriod, int checkInterval, CancellationToken cancellationToken)
  {
    ThrowIfDisposed();
    _logger.LogInformation("ファイル安定性チェッカーを開始しています...");
    _stabilizationPeriodSeconds = stabilizationPeriod;
    _stabilityCheckTimer = new Timer(CheckFileStabilityCallback, null, TimeSpan.FromMilliseconds(checkInterval), TimeSpan.FromMilliseconds(checkInterval));
    return Task.CompletedTask;
  }

  /// <summary>
  /// 安定性チェックを停止します。
  /// </summary>
  /// <param name="cancellationToken">キャンセルトークン</param>
  public Task StopAsync(CancellationToken cancellationToken)
  {
    ThrowIfDisposed();
    _logger.LogInformation("ファイル安定性チェッカーを停止しています...");
    _stabilityCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    return Task.CompletedTask;
  }

  /// <summary>
  /// タイマーコールバック
  /// </summary>
  private void CheckFileStabilityCallback(object? state)
  {
    if (_disposed) return;
    // 非同期メソッドを同期的に開始し、例外を適切に処理
    _ = CheckFileStabilityInternalAsync(CancellationToken.None);
  }

  /// <summary>
  /// ファイルの安定性をチェックする内部実装
  /// </summary>
  private async Task CheckFileStabilityInternalAsync(CancellationToken cancellationToken)
  {
    if (_disposed) return; // 再度チェック

    try
    {
      using (await _fileOperationLock.LockAsync(cancellationToken).ConfigureAwait(false))
      {
        if (_disposed) return; // ロック取得後にもチェック

        var now = DateTime.UtcNow;
        var stabilizationPeriod = TimeSpan.FromSeconds(_stabilizationPeriodSeconds);
        var filesToRemove = new List<string>();
        var stableFiles = new List<string>();

        // 安定性をチェックするファイルを選択
        var filesToCheck = _processingFiles.Where(kvp => (now - kvp.Value) >= stabilizationPeriod)
                                           .Select(kvp => kvp.Key)
                                           .ToList();

        if (!filesToCheck.Any())
        {
          return; // チェック対象なし
        }

        _logger.LogTrace("{Count}個のファイルの安定性をチェックします。", filesToCheck.Count);

        // 各ファイルの安定性を並列でチェック
        var checkTasks = filesToCheck.Select(async filePath =>
        {
          try
          {
            bool isStable = await IsFileAccessibleAsync(filePath, cancellationToken).ConfigureAwait(false);
            return (FilePath: filePath, IsStable: isStable);
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "ファイルの安定性チェック中にエラーが発生しました: {FilePath}", filePath);
            return (FilePath: filePath, IsStable: false); // エラー時は不安定とみなす
          }
        }).ToList();

        var results = await Task.WhenAll(checkTasks).ConfigureAwait(false);

        // 結果を処理
        foreach (var result in results)
        {
          if (result.IsStable)
          {
            _logger.LogDebug("ファイルが安定しました: {FilePath}", result.FilePath);
            stableFiles.Add(result.FilePath);
          }
          // 安定していなくても、チェック対象になったファイルは一旦リストから削除する
          // （再度変更があれば TrackFile で追加される）
          filesToRemove.Add(result.FilePath);
        }

        // 安定したファイルに対するイベントを発火
        foreach (var stableFilePath in stableFiles)
        {
          try
          {
            FileStabilized?.Invoke(this, new FileSystemEventArgs(
                WatcherChangeTypes.Changed, // 安定化は変更の一種とみなす
                Path.GetDirectoryName(stableFilePath) ?? string.Empty,
                Path.GetFileName(stableFilePath)));
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "ファイル安定化イベントの処理中にエラーが発生しました: {FilePath}", stableFilePath);
          }
        }

        // 処理済みのファイルをリストから削除
        foreach (var file in filesToRemove)
        {
          _processingFiles.TryRemove(file, out _);
        }
        _logger.LogTrace("安定性チェック完了。{RemoveCount}個のファイルをリストから削除、{StableCount}個の安定化イベントを発火。", filesToRemove.Count, stableFiles.Count);
      }
    }
    catch (OperationCanceledException)
    {
      _logger.LogInformation("ファイル安定性チェックがキャンセルされました。");
    }
    catch (ObjectDisposedException)
    {
      _logger.LogDebug("ファイル安定性チェック中にオブジェクトが破棄されました。");
    }
    catch (Exception ex)
    {
      // 予期しないエラー
      _logger.LogError(ex, "ファイル安定性チェックの実行中に予期しないエラーが発生しました");
    }
  }


  /// <summary>
  /// ファイルがアクセス可能かどうかを非同期でチェックするメソッド
  /// </summary>
  private async Task<bool> IsFileAccessibleAsync(string filePath, CancellationToken cancellationToken)
  {
    const int MaxRetries = 3;
    const int DelayMilliseconds = 100;

    for (int i = 0; i < MaxRetries; i++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        // ファイルが存在するかチェック
        if (!File.Exists(filePath))
        {
          _logger.LogWarning("安定性チェック中にファイルが見つかりませんでした: {FilePath}", filePath);
          return false; // 存在しないファイルは安定とはみなさない
        }

        // ファイルが読み取り可能かチェック（非同期版）
        // FileOptions.Asynchronous を使用
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite, // 他のプロセスが書き込み中でも読み取りは許可
            bufferSize: 1, // 最小限のバッファ
            options: FileOptions.Asynchronous);

        // 実際に読み取りを試みる (1バイトだけ)
        var buffer = new byte[1];
        int bytesRead = await stream.ReadAsync(buffer, 0, 1, cancellationToken).ConfigureAwait(false);

        // 読み取り成功（EOFでもOK）
        return true;
      }
      catch (IOException ex) when (i < MaxRetries - 1)
      {
        // ファイルがロックされている可能性。少し待ってリトライ
        _logger.LogTrace(ex, "ファイルアクセス中にIOExceptionが発生しました。リトライします ({Attempt}/{MaxAttempts}): {FilePath}", i + 1, MaxRetries, filePath);
        await Task.Delay(DelayMilliseconds, cancellationToken).ConfigureAwait(false);
      }
      catch (IOException ex)
      {
        _logger.LogWarning(ex, "ファイルアクセス中にIOExceptionが発生しました。リトライ上限に達しました: {FilePath}", filePath);
        return false; // リトライしてもアクセスできない場合は不安定
      }
      catch (OperationCanceledException)
      {
        throw; // キャンセルはそのままスロー
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "ファイルアクセスチェック中に予期しないエラーが発生しました: {FilePath}", filePath);
        return false; // その他のエラーの場合も不安定とみなす
      }
    }
    return false; // ここには到達しないはず
  }

  /// <summary>
  /// リソースのサイズを推定します
  /// </summary>
  protected override long EstimateResourceSize()
  {
    // TimerやDictionaryなどのリソースを考慮
    return 1 * 1024 * 1024; // 1MB程度と仮定
  }

  /// <summary>
  /// マネージドリソースを解放します
  /// </summary>
  protected override void ReleaseManagedResources()
  {
    if (!_disposed)
    {
      _logger.LogInformation("FileStabilityCheckerのリソースを解放します");
      _disposed = true; // Dispose開始

      // タイマーの停止と解放
      _stabilityCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
      _stabilityCheckTimer?.Dispose();
      _stabilityCheckTimer = null;

      // 内部コレクションのクリア
      _processingFiles.Clear();

      // ロックの解放
      _fileOperationLock.Dispose();

      // イベントハンドラのクリア
      FileStabilized = null;
    }
    // 基底クラスの処理を呼び出す
    base.ReleaseManagedResources();
  }

  /// <summary>
  /// マネージドリソースを非同期で解放します
  /// </summary>
  protected override async ValueTask ReleaseManagedResourcesAsync()
  {
    if (!_disposed)
    {
      _logger.LogInformation("FileStabilityCheckerのリソースを非同期で解放します");
      _disposed = true; // Dispose開始

      // タイマーの停止と解放
      _stabilityCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
      if (_stabilityCheckTimer is IAsyncDisposable asyncDisposableTimer)
      {
        await asyncDisposableTimer.DisposeAsync().ConfigureAwait(false);
      }
      else
      {
        _stabilityCheckTimer?.Dispose();
      }
      _stabilityCheckTimer = null;

      // 内部コレクションのクリア
      _processingFiles.Clear();

      // ロックの解放
      _fileOperationLock.Dispose(); // AsyncLockは同期Disposeのみ

      // イベントハンドラのクリア
      FileStabilized = null;
    }
    // 基底クラスの処理を呼び出す
    await base.ReleaseManagedResourcesAsync().ConfigureAwait(false);
  }

  // Disposeパターン用のヘルパーメソッド
  private void ThrowIfDisposed()
  {
    if (_disposed)
    {
      throw new ObjectDisposedException(typeof(FileStabilityChecker).FullName); // 修正
    }
  }
}
