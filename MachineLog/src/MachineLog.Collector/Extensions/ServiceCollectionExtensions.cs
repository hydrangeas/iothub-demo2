using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using MachineLog.Common.Logging;
using MachineLog.Common.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MachineLog.Collector.Extensions;

/// <summary>
/// IServiceCollectionの拡張メソッドを提供するクラス
/// </summary>
public static class ServiceCollectionExtensions
{
  /// <summary>
  /// Collectorサービスの設定を登録します
  /// </summary>
  /// <param name="services">サービスコレクション</param>
  /// <param name="configuration">構成情報</param>
  /// <returns>サービスコレクション</returns>
  public static IServiceCollection AddCollectorConfiguration(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    // 設定クラスをDIコンテナに登録
    services.Configure<CollectorConfig>(
      configuration.GetSection(nameof(CollectorConfig)));

    services.Configure<BatchConfig>(
      configuration.GetSection(nameof(BatchConfig)));

    services.Configure<IoTHubConfig>(
      configuration.GetSection(nameof(IoTHubConfig)));

    return services;
  }

  /// <summary>
  /// Collectorサービスで使用するサービスを登録します
  /// </summary>
  /// <param name="services">サービスコレクション</param>
  /// <returns>サービスコレクション</returns>
  public static IServiceCollection AddCollectorServices(
    this IServiceCollection services)
  {
    // ファイル監視サービスの登録
    services.AddSingleton<IFileStabilityChecker, FileStabilityChecker>(); // 追加
    services.AddSingleton<IFileWatcherService, FileWatcherService>();

    // ファイル処理関連サービスの登録
    services.AddTransient<EncodingDetector>();
    services.AddTransient<JsonLineProcessor>();
    services.AddTransient<IFileProcessorService, FileProcessorService>();

    // IoT Hubサービスの登録
    services.AddSingleton<IIoTHubService, IoTHubService>();

    // バッチ処理サービスの登録
    services.AddSingleton<IBatchProcessorService, BatchProcessorService>();

    // ファイル保持ポリシー関連サービスの登録
    services.AddSingleton<IFileRetentionService, FileRetentionService>();
    services.AddHostedService<FileRetentionHostedService>();

    // バリデーターの登録
    services.AddTransient<FluentValidation.IValidator<MachineLog.Common.Models.LogEntry>, MachineLog.Common.Validation.LogEntryValidator>();

    // エラーハンドリングとリトライ関連サービスの登録
    services.AddSingleton<Func<ILogger, StructuredLogger>>(loggerFactory =>
      logger => new StructuredLogger(logger));

    services.AddSingleton<Func<ILogger, RetryHandler>>(loggerFactory =>
      logger => new RetryHandler(logger));

    // ヘルスチェックの登録
    services.AddHealthChecks()
        .AddCheck<MachineLog.Collector.Health.IoTHubHealthCheck>("IoTHub")
        .AddCheck<MachineLog.Collector.Health.FileSystemHealthCheck>("FileSystem");

    return services;
  }
}
