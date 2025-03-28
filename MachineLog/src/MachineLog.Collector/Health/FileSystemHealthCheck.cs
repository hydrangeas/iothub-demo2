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
            var errorMessages = new List<string>();

            // 監視対象ディレクトリの存在確認
            foreach (var path in _config.MonitoringPaths)
            {
                var exists = Directory.Exists(path);
                data[$"Directory_{path}_Exists"] = exists;

                if (!exists)
                {
                    isHealthy = false;
                    var msg = $"監視対象ディレクトリが存在しません: {path}";
                    errorMessages.Add(msg);
                    _logger.LogWarning(msg);
                    // ディレクトリが存在しない場合、以降のチェックはスキップ
                    continue;
                }
                else
                {
                    // ディスク容量の確認
                    try
                    {
                        var pathRoot = Path.GetPathRoot(path);
                        if (string.IsNullOrEmpty(pathRoot))
                        {
                            isHealthy = false;
                            var msg = $"有効なドライブパスが取得できません: {path}";
                            errorMessages.Add(msg);
                            _logger.LogWarning(msg);
                            data[$"Drive_{path}_Error"] = "有効なパスルートが取得できません";
                            continue; // このパスに対する以降のチェックはスキップ
                        }

                        var driveInfo = new DriveInfo(pathRoot);
                        var freeSpaceGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        data[$"Drive_{driveInfo.Name}_FreeSpaceGB"] = Math.Round(freeSpaceGB, 2);

                        // 空き容量が最小閾値未満の場合は警告
                        if (freeSpaceGB < _config.FileSystemHealth.MinimumFreeDiskSpaceGB)
                        {
                            isHealthy = false;
                            var msg = $"ディスク容量が不足しています: {driveInfo.Name} ({Math.Round(freeSpaceGB, 2)} GB)";
                            errorMessages.Add(msg);
                            _logger.LogWarning(msg);
                        }
                    }
                    catch (Exception ex)
                    {
                        isHealthy = false;
                        var msg = $"ディスク容量の確認中にエラーが発生しました: {path}";
                        errorMessages.Add($"{msg} - {ex.Message}");
                        _logger.LogWarning(ex, msg);
                        // pathRootが取得できない場合もあるため、nullチェックを追加
                        var root = Path.GetPathRoot(path);
                        data[$"Drive_{(root ?? path)}_Error"] = ex.Message;
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
                        var msg = $"ディレクトリに書き込み権限がありません: {path}";
                        errorMessages.Add($"{msg} - {ex.Message}");
                        _logger.LogWarning(ex, msg);
                        data[$"Directory_{path}_Writable"] = false;
                        data[$"Directory_{path}_WriteError"] = ex.Message;
                    }
                }
            }

            if (isHealthy)
            {
                return Task.FromResult(HealthCheckResult.Healthy("ファイルシステムは正常です", data));
            }
            else
            {
                // 複数のエラーメッセージを結合
                var combinedErrorMessage = string.Join("; ", errorMessages);
                return Task.FromResult(HealthCheckResult.Degraded(combinedErrorMessage, null, data));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ファイルシステムヘルスチェック中にエラーが発生しました");
            return Task.FromResult(HealthCheckResult.Unhealthy("ファイルシステムヘルスチェック中にエラーが発生しました", ex));
        }
    }
}
