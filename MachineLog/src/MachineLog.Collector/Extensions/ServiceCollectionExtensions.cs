using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
    services.AddSingleton<IFileWatcherService, FileWatcherService>();

    // ファイル処理サービスの登録
    services.AddTransient<IFileProcessorService, FileProcessorService>();

    // バリデーターの登録
    services.AddTransient<FluentValidation.IValidator<MachineLog.Common.Models.LogEntry>, MachineLog.Common.Validation.LogEntryValidator>();

    return services;
  }
}
