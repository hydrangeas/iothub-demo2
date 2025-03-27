using MachineLog.Collector.Models;
using MachineLog.Common.Utilities;
using MachineLog.Common.Synchronization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.IO;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイル監視サービスの実装
/// </summary>
public class FileWatcherService : AsyncDisposableBase<FileWatcherService>, IFileWatcherService
{
  private readonly ILogger<FileWatcherService> _logger;
  private readonly CollectorConfig _config;
  private readonly ConcurrentDictionary<string, DirectoryWatcherConfig> _directoryConfigs = new();
  private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
  private readonly ConcurrentDictionary<string, DateTime> _processingFiles = new();
  private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
  private readonly AsyncLock _fileOperationLock = new AsyncLock();
  private Timer? _stabilityCheckTimer;
  private bool _isRunning;

  /// <summary>
  /// ファイル作成イベント
  /// </summary>
  public event EventHandler<FileSystemEventArgs>? FileCreated;

  /// <summary>
  /// ファイル変更イベント
  /// </summary>
  public event EventHandler<FileSystemEventArgs>? FileChanged;

  /// <summary>
  /// ファイル安定化イベント
  /// </summary>
  public event EventHandler<FileSystemEventArgs>? FileStabilized;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="config">設定</param>
  public FileWatcherService(
      ILogger<FileWatcherService> logger,
      IOptions<CollectorConfig> config) : base(true) // リソースマネージャーに登録
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _config = config?.Value ?? throw new ArgumentNullException(nameof(config));

    // 初期設定から監視ディレクトリを追加
    foreach (var path in _config.MonitoringPaths)
    {
      var dirConfig = new DirectoryWatcherConfig(path)
      {
        FileFilter = _config.FileFilter
      };
      _directoryConfigs.TryAdd(dirConfig.Id, dirConfig);
    }

    // DirectoryConfigsからも監視ディレクトリを追加
    foreach (var dirConfig in _config.DirectoryConfigs)
    {
      _directoryConfigs.TryAdd(dirConfig.Id, dirConfig);
    }
  }

  /// <summary>
  /// ファイル監視を開始します
  /// </summary>
  public Task StartAsync(CancellationToken cancellationToken)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    _logger.LogInformation("ファイル監視サービスを開始しています...");

    _configLock.EnterReadLock();
    try
    {
      if (_directoryConfigs.Count == 0)
      {
        _logger.LogWarning("監視対象のディレクトリが設定されていません");
        return Task.CompletedTask;
      }

      // 監視ディレクトリの数が上限を超えていないか確認
      if (_directoryConfigs.Count > _config.MaxDirectories)
      {
        _logger.LogWarning("監視ディレクトリの数が上限（{MaxDirectories}）を超えています。最初の{MaxDirectories}ディレクトリのみを監視します。",
            _config.MaxDirectories, _config.MaxDirectories);
      }

      // 各ディレクトリの監視を開始
      foreach (var configEntry in _directoryConfigs.Take(_config.MaxDirectories))
      {
        var config = configEntry.Value;
        if (!Directory.Exists(config.Path))
        {
          _logger.LogWarning("監視対象のディレクトリが存在しません: {Path}", config.Path);
          continue;
        }

        var watcher = new FileSystemWatcher(config.Path)
        {
          Filter = config.FileFilter,
          NotifyFilter = config.NotifyFilters,
          IncludeSubdirectories = config.IncludeSubdirectories,
          EnableRaisingEvents = true
        };

        watcher.Created += OnFileCreated;
        watcher.Changed += OnFileChanged;

        _watchers.TryAdd(configEntry.Key, watcher);
        _logger.LogInformation("ディレクトリの監視を開始しました: {Path}, フィルター: {Filter}", config.Path, config.FileFilter);
      }
    }
    finally
    {
      _configLock.ExitReadLock();
    }

    // ファイル安定性チェック用のタイマーを開始
    _stabilityCheckTimer = new Timer(CheckFileStability, null, 1000, 1000);
    _isRunning = true;

    return Task.CompletedTask;
  }

  /// <summary>
  /// ファイル監視を停止します
  /// </summary>
  public Task StopAsync(CancellationToken cancellationToken)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    _logger.LogInformation("ファイル監視サービスを停止しています...");

    _configLock.EnterWriteLock();
    try
    {
      foreach (var watcherEntry in _watchers)
      {
        var watcher = watcherEntry.Value;
        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnFileCreated;
        watcher.Changed -= OnFileChanged;
        watcher.Dispose();
      }

      _watchers.Clear();
      _processingFiles.Clear();

      _stabilityCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
      _stabilityCheckTimer?.Dispose();
      _stabilityCheckTimer = null;
      _isRunning = false;
    }
    finally
    {
      _configLock.ExitWriteLock();
    }

    return Task.CompletedTask;
  }

  /// <summary>
  /// 監視ディレクトリを追加します
  /// </summary>
  /// <param name="directoryPath">監視対象ディレクトリのパス</param>
  /// <returns>追加された監視設定の識別子</returns>
  public string AddWatchDirectory(string directoryPath)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    if (string.IsNullOrEmpty(directoryPath))
    {
      throw new ArgumentNullException(nameof(directoryPath));
    }

    var config = new DirectoryWatcherConfig(directoryPath)
    {
      FileFilter = _config.FileFilter
    };

    return AddWatchDirectory(config);
  }

  /// <summary>
  /// 監視ディレクトリを追加します
  /// </summary>
  /// <param name="config">監視設定</param>
  /// <returns>追加された監視設定の識別子</returns>
  public string AddWatchDirectory(DirectoryWatcherConfig config)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    if (config == null)
    {
      throw new ArgumentNullException(nameof(config));
    }

    _configLock.EnterUpgradeableReadLock();
    try
    {
      // 監視ディレクトリの数が上限に達しているか確認
      if (_directoryConfigs.Count >= _config.MaxDirectories)
      {
        _logger.LogWarning("監視ディレクトリの数が上限（{MaxDirectories}）に達しています。新しいディレクトリは追加できません。", _config.MaxDirectories);
        return string.Empty;
      }

      // 既に同じパスが監視されているか確認
      if (_directoryConfigs.Values.Any(c => c.Path.Equals(config.Path, StringComparison.OrdinalIgnoreCase)))
      {
        _logger.LogWarning("指定されたディレクトリは既に監視されています: {Path}", config.Path);
        return _directoryConfigs.First(c => c.Value.Path.Equals(config.Path, StringComparison.OrdinalIgnoreCase)).Key;
      }

      _configLock.EnterWriteLock();
      try
      {
        // 設定を追加
        _directoryConfigs.TryAdd(config.Id, config);

        // サービスが実行中の場合は、新しいディレクトリの監視を開始
        if (_isRunning)
        {
          if (!Directory.Exists(config.Path))
          {
            _logger.LogWarning("監視対象のディレクトリが存在しません: {Path}", config.Path);
            return config.Id;
          }

          var watcher = new FileSystemWatcher(config.Path)
          {
            Filter = config.FileFilter,
            NotifyFilter = config.NotifyFilters,
            IncludeSubdirectories = config.IncludeSubdirectories,
            EnableRaisingEvents = true
          };

          watcher.Created += OnFileCreated;
          watcher.Changed += OnFileChanged;

          _watchers.TryAdd(config.Id, watcher);
          _logger.LogInformation("ディレクトリの監視を開始しました: {Path}, フィルター: {Filter}", config.Path, config.FileFilter);
        }
      }
      finally
      {
        _configLock.ExitWriteLock();
      }
    }
    finally
    {
      _configLock.ExitUpgradeableReadLock();
    }

    return config.Id;
  }

  /// <summary>
  /// 監視ディレクトリを削除します
  /// </summary>
  /// <param name="directoryId">監視設定の識別子</param>
  /// <returns>削除に成功したかどうか</returns>
  public bool RemoveWatchDirectory(string directoryId)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    if (string.IsNullOrEmpty(directoryId))
    {
      throw new ArgumentNullException(nameof(directoryId));
    }

    _configLock.EnterWriteLock();
    try
    {
      // 設定が存在するか確認
      if (!_directoryConfigs.TryRemove(directoryId, out _))
      {
        _logger.LogWarning("指定された識別子の監視設定が見つかりません: {DirectoryId}", directoryId);
        return false;
      }

      // ウォッチャーが存在する場合は停止して削除
      if (_watchers.TryRemove(directoryId, out var watcher))
      {
        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnFileCreated;
        watcher.Changed -= OnFileChanged;
        watcher.Dispose();
        _logger.LogInformation("ディレクトリの監視を停止しました: {DirectoryId}", directoryId);
      }

      return true;
    }
    finally
    {
      _configLock.ExitWriteLock();
    }
  }

  /// <summary>
  /// 監視ディレクトリを削除します
  /// </summary>
  /// <param name="directoryPath">監視対象ディレクトリのパス</param>
  /// <returns>削除に成功したかどうか</returns>
  public bool RemoveWatchDirectoryByPath(string directoryPath)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    if (string.IsNullOrEmpty(directoryPath))
    {
      throw new ArgumentNullException(nameof(directoryPath));
    }

    _configLock.EnterUpgradeableReadLock();
    try
    {
      // パスに一致する設定を検索
      var configEntry = _directoryConfigs.FirstOrDefault(c => c.Value.Path.Equals(directoryPath, StringComparison.OrdinalIgnoreCase));
      if (configEntry.Key == null)
      {
        _logger.LogWarning("指定されたパスの監視設定が見つかりません: {DirectoryPath}", directoryPath);
        return false;
      }

      // 識別子を使用して削除
      string directoryId = configEntry.Key;
      return RemoveWatchDirectory(directoryId);
    }
    finally
    {
      _configLock.ExitUpgradeableReadLock();
    }
  }

  /// <summary>
  /// 現在監視中のディレクトリ設定のリストを取得します
  /// </summary>
  /// <returns>監視設定のリスト</returns>
  public IReadOnlyList<DirectoryWatcherConfig> GetWatchDirectories()
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    // スレッドセーフな読み取り
    _configLock.EnterReadLock();
    try
    {
      return _directoryConfigs.Values.ToList();
    }
    finally
    {
      _configLock.ExitReadLock();
    }
  }

  /// <summary>
  /// ファイル作成イベントハンドラ
  /// </summary>
  private void OnFileCreated(object sender, FileSystemEventArgs e)
  {
    // オブジェクトが破棄済みの場合は処理をスキップ
    if (_disposed)
      return;

    _logger.LogDebug("ファイル作成イベントを検出しました: {FilePath}", e.FullPath);

    // ファイル拡張子のフィルタリング
    if (_config.FileExtensions.Count > 0 &&
        !_config.FileExtensions.Contains(Path.GetExtension(e.FullPath).ToLowerInvariant()))
    {
      _logger.LogDebug("対象外のファイル拡張子のため無視します: {FilePath}", e.FullPath);
      return;
    }

    // ファイルの最終更新時刻を記録（スレッドセーフな操作）
    _processingFiles[e.FullPath] = DateTime.UtcNow;

    // イベントを発火
    try
    {
      FileCreated?.Invoke(this, e);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイル作成イベントの処理中にエラーが発生しました: {FilePath}", e.FullPath);
    }
  }

  /// <summary>
  /// ファイル変更イベントハンドラ
  /// </summary>
  private void OnFileChanged(object sender, FileSystemEventArgs e)
  {
    // オブジェクトが破棄済みの場合は処理をスキップ
    if (_disposed)
      return;

    _logger.LogDebug("ファイル変更イベントを検出しました: {FilePath}", e.FullPath);

    // ファイル拡張子のフィルタリング
    if (_config.FileExtensions.Count > 0 &&
        !_config.FileExtensions.Contains(Path.GetExtension(e.FullPath).ToLowerInvariant()))
    {
      _logger.LogDebug("対象外のファイル拡張子のため無視します: {FilePath}", e.FullPath);
      return;
    }

    // ファイルの最終更新時刻を更新（スレッドセーフな操作）
    _processingFiles[e.FullPath] = DateTime.UtcNow;

    // イベントを発火
    try
    {
      FileChanged?.Invoke(this, e);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイル変更イベントの処理中にエラーが発生しました: {FilePath}", e.FullPath);
    }
  }

  /// <summary>
  /// ファイルの安定性をチェックするメソッド
  /// </summary>
  private void CheckFileStability(object? state)
  {
    // オブジェクトが破棄済みの場合は処理をスキップ
    if (_disposed)
      return;

    // 非同期メソッドを同期的に開始し、例外を適切に処理
    _ = CheckFileStabilityInternalAsync(CancellationToken.None);
  }

  /// <summary>
  /// ファイルの安定性をチェックする内部実装
  /// </summary>
  private async Task CheckFileStabilityInternalAsync(CancellationToken cancellationToken)
  {
    try
    {
      // 非同期ロックを使用して同時実行を防止
      using (await _fileOperationLock.LockAsync(cancellationToken))
      {
        try
        {
          var now = DateTime.UtcNow;
          var stabilizationPeriod = TimeSpan.FromSeconds(_config.StabilizationPeriodSeconds);
          var filesToRemove = new List<string>();

          // 並列処理の最適化
          var fileCheckTasks = new List<Task<(string FilePath, bool IsStable)>>();

          // 各ファイルの安定性を並列でチェック
          foreach (var file in _processingFiles)
          {
            var lastModified = file.Value;
            var timeSinceLastModification = now - lastModified;

            if (timeSinceLastModification >= stabilizationPeriod)
            {
              var filePath = file.Key;
              fileCheckTasks.Add(Task.Run(async () =>
              {
                try
                {
                  // ファイルが存在し、アクセス可能かチェック
                  bool isStable = await IsFileAccessibleAsync(filePath);
                  return (filePath, isStable);
                }
                catch (Exception ex)
                {
                  _logger.LogWarning(ex, "ファイルの安定性チェック中にエラーが発生しました: {FilePath}", filePath);
                  return (filePath, false);
                }
              }));
            }
          }

          // すべてのチェックが完了するのを待機
          if (fileCheckTasks.Count > 0)
          {
            var results = await Task.WhenAll(fileCheckTasks);

            // 安定したファイルを処理
            foreach (var result in results)
            {
              if (result.IsStable)
              {
                _logger.LogDebug("ファイルが安定しました: {FilePath}", result.FilePath);

                try
                {
                  // ファイル安定化イベントを発火
                  FileStabilized?.Invoke(this, new FileSystemEventArgs(
                      WatcherChangeTypes.Changed,
                      Path.GetDirectoryName(result.FilePath) ?? string.Empty,
                      Path.GetFileName(result.FilePath)));
                }
                catch (Exception ex)
                {
                  _logger.LogError(ex, "ファイル安定化イベントの処理中にエラーが発生しました: {FilePath}", result.FilePath);
                }

                filesToRemove.Add(result.FilePath);
              }
            }

            // 処理済みのファイルをリストから削除
            foreach (var file in filesToRemove)
            {
              _processingFiles.TryRemove(file, out _);
            }
          }
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ファイル安定性チェック処理中に予期しないエラーが発生しました");
        }
      }
    }
    catch (Exception ex)
    {
      // 外部例外をログに記録
      _logger.LogError(ex, "ファイル安定性チェックの実行中に予期しないエラーが発生しました");
    }
  }

  /// <summary>
  /// ファイルがアクセス可能かどうかを非同期でチェックするメソッド
  /// </summary>
  private async Task<bool> IsFileAccessibleAsync(string filePath)
  {
    try
    {
      // ファイルが存在するかチェック
      if (!File.Exists(filePath))
      {
        return false;
      }

      // ファイルが読み取り可能かチェック（非同期版）
      using var stream = new FileStream(
          filePath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.ReadWrite,
          4096,
          FileOptions.Asynchronous);

      // 実際に読み取りを試みる
      var buffer = new byte[1];
      await stream.ReadAsync(buffer, 0, 1);

      return true;
    }
    catch (IOException)
    {
      // ファイルがロックされている場合
      return false;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "ファイルアクセスチェック中にエラーが発生しました: {FilePath}", filePath);
      return false;
    }
  }

  /// <summary>
  /// リソースのサイズを推定します
  /// </summary>
  /// <returns>推定サイズ（バイト単位）</returns>
  protected override long EstimateResourceSize()
  {
    // FileSystemWatcherやTimerなどのリソースを考慮
    return 2 * 1024 * 1024; // 2MB
  }

  /// <summary>
  /// マネージドリソースを解放します
  /// </summary>
  protected override void ReleaseManagedResources()
  {
    _logger.LogInformation("FileWatcherServiceのリソースを解放します");

    try
    {
      // 監視中のファイルウォッチャーをすべて解放
      foreach (var watcherEntry in _watchers)
      {
        try
        {
          var watcher = watcherEntry.Value;
          watcher.EnableRaisingEvents = false;
          watcher.Created -= OnFileCreated;
          watcher.Changed -= OnFileChanged;
          watcher.Dispose();
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "FileSystemWatcherの解放中にエラーが発生しました: {WatcherId}", watcherEntry.Key);
        }
      }

      _watchers.Clear();

      // タイマーの停止と解放
      if (_stabilityCheckTimer != null)
      {
        _stabilityCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _stabilityCheckTimer.Dispose();
        _stabilityCheckTimer = null;
      }

      // 内部コレクションのクリア
      _processingFiles.Clear();
      _directoryConfigs.Clear();

      // ロックの解放
      _configLock.Dispose();
      _fileOperationLock.Dispose();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "FileWatcherServiceのリソース解放中にエラーが発生しました");
    }

    _isRunning = false;

    // 基底クラスの処理を呼び出す
    base.ReleaseManagedResources();
  }

  /// <summary>
  /// マネージドリソースを非同期で解放します
  /// </summary>
  protected override async ValueTask ReleaseManagedResourcesAsync()
  {
    _logger.LogInformation("FileWatcherServiceのリソースを非同期で解放します");

    try
    {
      // 監視中のファイルウォッチャーをすべて解放
      foreach (var watcherEntry in _watchers)
      {
        try
        {
          var watcher = watcherEntry.Value;
          watcher.EnableRaisingEvents = false;
          watcher.Created -= OnFileCreated;
          watcher.Changed -= OnFileChanged;
          watcher.Dispose();
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "FileSystemWatcherの非同期解放中にエラーが発生しました: {WatcherId}", watcherEntry.Key);
        }
      }

      _watchers.Clear();

      // タイマーの停止と解放
      if (_stabilityCheckTimer != null)
      {
        _stabilityCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _stabilityCheckTimer.Dispose();
        _stabilityCheckTimer = null;
      }

      // 内部コレクションのクリア
      _processingFiles.Clear();
      _directoryConfigs.Clear();

      // ロックの解放
      _configLock.Dispose();
      _fileOperationLock.Dispose();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "FileWatcherServiceのリソース非同期解放中にエラーが発生しました");
    }

    _isRunning = false;

    // 基底クラスの処理を呼び出す
    await base.ReleaseManagedResourcesAsync().ConfigureAwait(false);
  }
}
