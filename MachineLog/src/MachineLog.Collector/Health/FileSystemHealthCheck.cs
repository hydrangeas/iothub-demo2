using MachineLog.Collector.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MachineLog.Collector.Health;

/// <summary>
/// ファイルシステムの状態を確認するヘルスチェック
/// </summary>
public class FileSystemHealthCheck : IHealthCheck
{
    private readonly CollectorConfig _config;
    private readonly ILogger<FileSystemHealthCheck> _logger;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="config">設定</param>
    /// <param name="logger">ロガー</param>
    public FileSystemHealthCheck(IOptions<CollectorConfig> config, ILogger<FileSystemHealthCheck> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// ヘルスチェックを実行します
    /// </summary>
    /// <param name="context">ヘルスチェックコンテキスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>ヘルスチェック結果</returns>
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object>();
            var isHealthy = true;
            var statusMessage = "ファイルシステムは正常です";

            // 監視対象ディレクトリの存在確認
            foreach (var path in _config.MonitoringPaths)
            {
                var exists = Directory.Exists(path);
                data[$"Directory_{path}_Exists"] = exists;

                if (!exists)
                {
                    isHealthy = false;
                    statusMessage = $"監視対象ディレクトリが存在しません: {path}";
                    _logger.LogWarning("監視対象ディレクトリが存在しません: {Path}", path);
                }
                else
                {
                    // ディスク容量の確認
                    try
                    {
                        var pathRoot = Path.GetPathRoot(path);
                        if (string.IsNullOrEmpty(pathRoot))
                        {
                            _logger.LogWarning("有効なドライブパスが取得できません: {Path}", path);
                            isHealthy = false;
                            statusMessage = $"有効なドライブパスが取得できません: {path}";
                            data[$"Drive_{path}_Error"] = "有効なパスルートが取得できません";
                            continue;
                        }

                        var driveInfo = new DriveInfo(pathRoot);
                        var freeSpaceGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        data[$"Drive_{driveInfo.Name}_FreeSpaceGB"] = Math.Round(freeSpaceGB, 2);

                        // 空き容量が最小閾値未満の場合は警告
                        if (freeSpaceGB < _config.FileSystemHealth.MinimumFreeDiskSpaceGB)
                        {
                            isHealthy = false;
                            statusMessage = $"ディスク容量が不足しています: {driveInfo.Name} ({Math.Round(freeSpaceGB, 2)} GB)";
                            _logger.LogWarning("ディスク容量が不足しています: {Drive} ({FreeSpace:0.00} GB)",
                                driveInfo.Name, freeSpaceGB);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "ディスク容量の確認中にエラーが発生しました: {Path}", path);
                        data[$"Drive_{Path.GetPathRoot(path)}_Error"] = ex.Message;
                    }

                    // 書き込み権限の確認
                    try
                    {
                        var testFilePath = Path.Combine(path, $"healthcheck_{Guid.NewGuid()}.tmp");
                        File.WriteAllText(testFilePath, "test");
                        File.Delete(testFilePath);
                        data[$"Directory_{path}_Writable"] = true;
                    }
                    catch (Exception ex)
                    {
                        isHealthy = false;
                        statusMessage = $"ディレクトリに書き込み権限がありません: {path}";
                        _logger.LogWarning(ex, "ディレクトリに書き込み権限がありません: {Path}", path);
                        data[$"Directory_{path}_Writable"] = false;
                        data[$"Directory_{path}_WriteError"] = ex.Message;
                    }
                }
            }

            if (isHealthy)
            {
                return Task.FromResult(HealthCheckResult.Healthy(statusMessage, data));
            }
            else
            {
                return Task.FromResult(HealthCheckResult.Degraded(statusMessage, null, data));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ファイルシステムヘルスチェック中にエラーが発生しました");
            return Task.FromResult(HealthCheckResult.Unhealthy("ファイルシステムヘルスチェック中にエラーが発生しました", ex));
        }
    }
}
