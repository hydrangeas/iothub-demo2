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
  // private readonly ConcurrentDictionary<string, DateTime> _processingFiles = new(); // 削除
  private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
  // private readonly AsyncLock _fileOperationLock = new AsyncLock(); // 削除
  // private Timer? _stabilityCheckTimer; // 削除
  private readonly IFileStabilityChecker _fileStabilityChecker; // 追加
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
  /// <param name="fileStabilityChecker">ファイル安定性チェッカー</param> // 追加
  public FileWatcherService(
      ILogger<FileWatcherService> logger,
      IOptions<CollectorConfig> config,
      IFileStabilityChecker fileStabilityChecker) : base(true) // リソースマネージャーに登録
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    _fileStabilityChecker = fileStabilityChecker ?? throw new ArgumentNullException(nameof(fileStabilityChecker)); // 追加

    // ファイル安定化イベントを中継
    _fileStabilityChecker.FileStabilized += OnFileStabilizedInternal; // 追加

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
        StartWatchingDirectoryInternal(configEntry.Key, configEntry.Value);
      }
    }
    finally
    {
      _configLock.ExitReadLock();
    }

    // ファイル安定性チェッカーを開始 (checkIntervalは仮に1000ms)
    _ = _fileStabilityChecker.StartAsync(_config.StabilizationPeriodSeconds, 1000, cancellationToken); // 変更
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
      // すべてのウォッチャーを停止
      foreach (var directoryId in _watchers.Keys.ToList()) // ToList() でコピーを作成
      {
        StopWatchingDirectoryInternal(directoryId);
      }

      // _processingFiles.Clear(); // FileStabilityCheckerが管理

      // 安定性チェッカーを停止
      _ = _fileStabilityChecker.StopAsync(cancellationToken); // 変更
      // _stabilityCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite); // 削除
      // _stabilityCheckTimer?.Dispose(); // 削除
      // _stabilityCheckTimer = null; // 削除
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
          StartWatchingDirectoryInternal(config.Id, config);
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
      StopWatchingDirectoryInternal(directoryId);

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

    // 拡張子フィルタリング
    if (!IsTargetFileExtension(e.FullPath))
    {
      return;
    }

    // ファイル安定性チェッカーに追跡を依頼
    _fileStabilityChecker.TrackFile(e.FullPath); // 変更

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

    // 拡張子フィルタリング
    if (!IsTargetFileExtension(e.FullPath))
    {
      return;
    }

    // ファイル安定性チェッカーに追跡を依頼
    _fileStabilityChecker.TrackFile(e.FullPath); // 変更

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
  /// FileStabilityCheckerからのファイル安定化イベントを中継します。
  /// </summary>
  private void OnFileStabilizedInternal(object? sender, FileSystemEventArgs e)
  {
    // オブジェクトが破棄済みの場合は処理をスキップ
    if (_disposed)
      return;

    try
    {
      // FileWatcherServiceのFileStabilizedイベントを発火
      FileStabilized?.Invoke(this, e);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイル安定化イベントの中継処理中にエラーが発生しました: {FilePath}", e.FullPath);
    }
  }

  // CheckFileStability, CheckFileStabilityInternalAsync, IsFileAccessibleAsync は削除

  /// <summary>
  /// 指定されたファイルパスが監視対象の拡張子を持つかどうかを判断します。
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  /// <returns>監視対象の場合は true、それ以外は false</returns>
  private bool IsTargetFileExtension(string filePath)
  {
    if (_config.FileExtensions.Count == 0)
    {
      return true; // 拡張子フィルタリングが無効な場合は常に true
    }

    var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
    if (string.IsNullOrEmpty(extension) || !_config.FileExtensions.Contains(extension))
    {
      _logger.LogDebug("対象外のファイル拡張子のため無視します: {FilePath}", filePath);
      return false;
    }
    return true;
  }

  /// <summary>
  /// 指定されたディレクトリの監視を開始する内部メソッド。
  /// </summary>
  /// <param name="directoryId">監視設定の識別子</param>
  /// <param name="config">監視設定</param>
  private void StartWatchingDirectoryInternal(string directoryId, DirectoryWatcherConfig config)
  {
    if (!Directory.Exists(config.Path))
    {
      _logger.LogWarning("監視対象のディレクトリが存在しません: {Path}", config.Path);
      return;
    }

    // 既に監視中の場合は何もしない
    if (_watchers.ContainsKey(directoryId))
    {
      _logger.LogDebug("ディレクトリは既に監視中です: {Path}", config.Path);
      return;
    }

    try
    {
      var watcher = new FileSystemWatcher(config.Path)
      {
        Filter = config.FileFilter,
        NotifyFilter = config.NotifyFilters,
        IncludeSubdirectories = config.IncludeSubdirectories,
        EnableRaisingEvents = true
      };

      watcher.Created += OnFileCreated;
      watcher.Changed += OnFileChanged;

      if (_watchers.TryAdd(directoryId, watcher))
      {
        _logger.LogInformation("ディレクトリの監視を開始しました: {Path}, フィルター: {Filter}", config.Path, config.FileFilter);
      }
      else
      {
        // 追加に失敗した場合（通常は発生しないはず）
        watcher.Dispose();
        _logger.LogWarning("FileSystemWatcherの追加に失敗しました: {DirectoryId}", directoryId);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ディレクトリの監視開始中にエラーが発生しました: {Path}", config.Path);
    }
  }

  /// <summary>
  /// 指定されたディレクトリの監視を停止する内部メソッド。
  /// </summary>
  /// <param name="directoryId">監視設定の識別子</param>
  private void StopWatchingDirectoryInternal(string directoryId)
  {
    if (_watchers.TryRemove(directoryId, out var watcher))
    {
      try
      {
        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnFileCreated;
        watcher.Changed -= OnFileChanged;
        watcher.Dispose();
        _logger.LogInformation("ディレクトリの監視を停止しました: {DirectoryId}", directoryId);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "FileSystemWatcherの停止中にエラーが発生しました: {DirectoryId}", directoryId);
      }
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
      // すべてのウォッチャーを停止して解放
      foreach (var directoryId in _watchers.Keys.ToList()) // ToList() でコピーを作成
      {
        StopWatchingDirectoryInternal(directoryId);
      }
      _watchers.Clear(); // StopWatchingDirectoryInternal で Remove されるが念のため

      // タイマーの停止と解放 (FileStabilityCheckerが管理)
      // if (_stabilityCheckTimer != null) ...

      // 内部コレクションのクリア
      // _processingFiles.Clear(); // FileStabilityCheckerが管理
      _directoryConfigs.Clear();

      // ロックの解放
      _configLock.Dispose();
      // _fileOperationLock.Dispose(); // FileStabilityCheckerが管理

      // イベントハンドラの解除
      if (_fileStabilityChecker != null)
      {
        _fileStabilityChecker.FileStabilized -= OnFileStabilizedInternal;
      }
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
      // すべてのウォッチャーを停止して解放
      foreach (var directoryId in _watchers.Keys.ToList()) // ToList() でコピーを作成
      {
        StopWatchingDirectoryInternal(directoryId);
      }
      _watchers.Clear(); // StopWatchingDirectoryInternal で Remove されるが念のため

      // タイマーの停止と解放 (FileStabilityCheckerが管理)
      // if (_stabilityCheckTimer != null) ...

      // 内部コレクションのクリア
      // _processingFiles.Clear(); // FileStabilityCheckerが管理
      _directoryConfigs.Clear();

      // ロックの解放
      _configLock.Dispose();
      // _fileOperationLock.Dispose(); // FileStabilityCheckerが管理

      // イベントハンドラの解除
      if (_fileStabilityChecker != null)
      {
        _fileStabilityChecker.FileStabilized -= OnFileStabilizedInternal;
      }
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
