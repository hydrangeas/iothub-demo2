using MachineLog.Collector.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.IO;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイル監視サービスの実装
/// </summary>
public class FileWatcherService : IFileWatcherService, IDisposable
{
  private readonly ILogger<FileWatcherService> _logger;
  private readonly CollectorConfig _config;
  private readonly List<FileSystemWatcher> _watchers = new();
  private readonly ConcurrentDictionary<string, DateTime> _processingFiles = new();
  private Timer? _stabilityCheckTimer;
  private bool _isDisposed;

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
      IOptions<CollectorConfig> config)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
  }

  /// <summary>
  /// ファイル監視を開始します
  /// </summary>
  public Task StartAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("ファイル監視サービスを開始しています...");

    if (_config.MonitoringPaths.Count == 0)
    {
      _logger.LogWarning("監視対象のディレクトリが設定されていません");
      return Task.CompletedTask;
    }

    foreach (var path in _config.MonitoringPaths)
    {
      if (!Directory.Exists(path))
      {
        _logger.LogWarning("監視対象のディレクトリが存在しません: {Path}", path);
        continue;
      }

      var watcher = new FileSystemWatcher(path)
      {
        Filter = _config.FileFilter,
        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        IncludeSubdirectories = true,
        EnableRaisingEvents = true
      };

      watcher.Created += OnFileCreated;
      watcher.Changed += OnFileChanged;

      _watchers.Add(watcher);
      _logger.LogInformation("ディレクトリの監視を開始しました: {Path}, フィルター: {Filter}", path, _config.FileFilter);
    }

    // ファイル安定性チェック用のタイマーを開始
    _stabilityCheckTimer = new Timer(CheckFileStability, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

    return Task.CompletedTask;
  }

  /// <summary>
  /// ファイル監視を停止します
  /// </summary>
  public Task StopAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("ファイル監視サービスを停止しています...");

    foreach (var watcher in _watchers)
    {
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

    return Task.CompletedTask;
  }

  /// <summary>
  /// ファイル作成イベントハンドラ
  /// </summary>
  private void OnFileCreated(object sender, FileSystemEventArgs e)
  {
    _logger.LogDebug("ファイル作成イベントを検出しました: {FilePath}", e.FullPath);

    // ファイルの最終更新時刻を記録
    _processingFiles[e.FullPath] = DateTime.UtcNow;

    // イベントを発火
    FileCreated?.Invoke(this, e);
  }

  /// <summary>
  /// ファイル変更イベントハンドラ
  /// </summary>
  private void OnFileChanged(object sender, FileSystemEventArgs e)
  {
    _logger.LogDebug("ファイル変更イベントを検出しました: {FilePath}", e.FullPath);

    // ファイルの最終更新時刻を更新
    _processingFiles[e.FullPath] = DateTime.UtcNow;

    // イベントを発火
    FileChanged?.Invoke(this, e);
  }

  /// <summary>
  /// ファイルの安定性をチェックするメソッド
  /// </summary>
  private void CheckFileStability(object? state)
  {
    var now = DateTime.UtcNow;
    var stabilizationPeriod = TimeSpan.FromSeconds(_config.StabilizationPeriodSeconds);
    var filesToRemove = new List<string>();

    foreach (var file in _processingFiles)
    {
      var lastModified = file.Value;
      var timeSinceLastModification = now - lastModified;

      if (timeSinceLastModification >= stabilizationPeriod)
      {
        try
        {
          // ファイルが存在し、アクセス可能かチェック
          if (IsFileAccessible(file.Key))
          {
            _logger.LogDebug("ファイルが安定しました: {FilePath}", file.Key);

            // ファイル安定化イベントを発火
            FileStabilized?.Invoke(this, new FileSystemEventArgs(
                WatcherChangeTypes.Changed,
                Path.GetDirectoryName(file.Key) ?? string.Empty,
                Path.GetFileName(file.Key)));

            filesToRemove.Add(file.Key);
          }
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "ファイルの安定性チェック中にエラーが発生しました: {FilePath}", file.Key);
        }
      }
    }

    // 処理済みのファイルをリストから削除
    foreach (var file in filesToRemove)
    {
      _processingFiles.TryRemove(file, out _);
    }
  }

  /// <summary>
  /// ファイルがアクセス可能かどうかをチェックするメソッド
  /// </summary>
  private bool IsFileAccessible(string filePath)
  {
    try
    {
      // ファイルが存在するかチェック
      if (!File.Exists(filePath))
      {
        return false;
      }

      // ファイルが読み取り可能かチェック
      using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
  /// リソースを解放します
  /// </summary>
  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// リソースを解放します
  /// </summary>
  protected virtual void Dispose(bool disposing)
  {
    if (_isDisposed)
    {
      return;
    }

    if (disposing)
    {
      foreach (var watcher in _watchers)
      {
        watcher.Dispose();
      }

      _watchers.Clear();
      _stabilityCheckTimer?.Dispose();
    }

    _isDisposed = true;
  }
}