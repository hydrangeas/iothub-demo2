using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MachineLog.Collector.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイル保持ポリシーを定期実行するホステッドサービス
/// </summary>
public class FileRetentionHostedService : BackgroundService
{
  private readonly ILogger<FileRetentionHostedService> _logger;
  private readonly CollectorConfig _config;
  private readonly IFileRetentionService _fileRetentionService;
  private readonly TimeSpan _executionInterval = TimeSpan.FromHours(6);
  private readonly TimeSpan _diskCheckInterval = TimeSpan.FromMinutes(30);
  private DateTime _lastCleanupTime = DateTime.MinValue;
  private DateTime _lastDiskCheckTime = DateTime.MinValue;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="options">コレクター設定</param>
  /// <param name="fileRetentionService">ファイル保持サービス</param>
  public FileRetentionHostedService(
    ILogger<FileRetentionHostedService> logger,
    IOptions<CollectorConfig> options,
    IFileRetentionService fileRetentionService)
  {
    _logger = logger;
    _config = options.Value;
    _fileRetentionService = fileRetentionService;
  }

  /// <summary>
  /// バックグラウンドサービスを実行します
  /// </summary>
  /// <param name="stoppingToken">キャンセルトークン</param>
  /// <returns>Task</returns>
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("ファイル保持ポリシーバックグラウンドサービスを開始しました");

    try
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        var now = DateTime.Now;

        // ディスク容量チェック（30分ごと）
        if ((now - _lastDiskCheckTime) > _diskCheckInterval)
        {
          await CheckAllDirectoriesDiskSpaceAsync();
          _lastDiskCheckTime = now;
        }

        // 定期クリーンアップ（6時間ごと）
        if ((now - _lastCleanupTime) > _executionInterval)
        {
          await CleanupAllDirectoriesAsync();
          _lastCleanupTime = now;
        }

        // 1分待機
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
      }
    }
    catch (OperationCanceledException)
    {
      // 正常な終了
      _logger.LogInformation("ファイル保持ポリシーバックグラウンドサービスが停止しました");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイル保持ポリシーバックグラウンドサービスでエラーが発生しました");
    }
  }

  /// <summary>
  /// すべての監視ディレクトリのディスク容量をチェックします
  /// </summary>
  /// <returns>Task</returns>
  private async Task CheckAllDirectoriesDiskSpaceAsync()
  {
    _logger.LogInformation("ディスク容量チェックを開始します");

    try
    {
      var directories = GetMonitoringDirectories();

      foreach (var directory in directories)
      {
        try
        {
          var isLowDiskSpace = await _fileRetentionService.CheckDiskSpaceAsync(directory);

          if (isLowDiskSpace)
          {
            // ディスク容量が不足している場合は緊急クリーンアップを実行
            _logger.LogWarning("ディスク容量不足を検出しました。緊急クリーンアップを実行します: {Directory}", directory);
            await _fileRetentionService.EmergencyCleanupAsync(directory);
          }
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ディレクトリのディスク容量チェック中にエラーが発生しました: {Directory}", directory);
        }
      }

      _logger.LogInformation("ディスク容量チェックが完了しました");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ディスク容量チェック処理でエラーが発生しました");
    }
  }

  /// <summary>
  /// すべての監視ディレクトリをクリーンアップします
  /// </summary>
  /// <returns>Task</returns>
  private async Task CleanupAllDirectoriesAsync()
  {
    _logger.LogInformation("定期クリーンアップを開始します");

    try
    {
      var directories = GetMonitoringDirectories();

      foreach (var directory in directories)
      {
        try
        {
          await _fileRetentionService.CleanupAsync(directory);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ディレクトリのクリーンアップ中にエラーが発生しました: {Directory}", directory);
        }
      }

      _logger.LogInformation("定期クリーンアップが完了しました");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "クリーンアップ処理でエラーが発生しました");
    }
  }

  /// <summary>
  /// 監視対象のディレクトリリストを取得します
  /// </summary>
  /// <returns>監視ディレクトリのリスト</returns>
  private string[] GetMonitoringDirectories()
  {
    var directories = new List<string>();

    // MonitoringPathsから取得
    if (_config.MonitoringPaths != null && _config.MonitoringPaths.Count > 0)
    {
      directories.AddRange(_config.MonitoringPaths);
    }

    // DirectoryConfigsから取得
    if (_config.DirectoryConfigs != null && _config.DirectoryConfigs.Count > 0)
    {
      directories.AddRange(_config.DirectoryConfigs.Select(c => c.Path));
    }

    // 重複を除去
    return directories.Distinct().ToArray();
  }
}
