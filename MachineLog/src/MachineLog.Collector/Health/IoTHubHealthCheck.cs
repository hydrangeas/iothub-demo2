using MachineLog.Collector.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace MachineLog.Collector.Health;

/// <summary>
/// IoT Hubの接続状態を確認するヘルスチェック
/// </summary>
public class IoTHubHealthCheck : IHealthCheck
{
    private readonly IIoTHubService _iotHubService;
    private readonly ILogger<IoTHubHealthCheck> _logger;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="iotHubService">IoT Hubサービス</param>
    /// <param name="logger">ロガー</param>
    public IoTHubHealthCheck(IIoTHubService iotHubService, ILogger<IoTHubHealthCheck> logger)
    {
        _iotHubService = iotHubService ?? throw new ArgumentNullException(nameof(iotHubService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// ヘルスチェックを実行します
    /// </summary>
    /// <param name="context">ヘルスチェックコンテキスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>ヘルスチェック結果</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionState = _iotHubService.GetConnectionState();
            
            if (connectionState == ConnectionState.Connected)
            {
                return HealthCheckResult.Healthy("IoT Hubに接続されています");
            }
            
            if (connectionState == ConnectionState.Connecting)
            {
                return HealthCheckResult.Degraded("IoT Hubに接続中です");
            }
            
            if (connectionState == ConnectionState.Error)
            {
                return HealthCheckResult.Unhealthy("IoT Hub接続でエラーが発生しています");
            }
            
            // 切断状態の場合は接続を試みる
            _logger.LogInformation("ヘルスチェックのためにIoT Hubへの接続を試みます");
            var result = await _iotHubService.ConnectAsync(cancellationToken);
            
            if (result.Success)
            {
                return HealthCheckResult.Healthy("IoT Hubに接続されています");
            }
            
            return HealthCheckResult.Unhealthy($"IoT Hubに接続できません: {result.ErrorMessage}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IoT Hubヘルスチェック中にエラーが発生しました");
            return HealthCheckResult.Unhealthy("IoT Hubヘルスチェック中にエラーが発生しました", ex);
        }
    }
}