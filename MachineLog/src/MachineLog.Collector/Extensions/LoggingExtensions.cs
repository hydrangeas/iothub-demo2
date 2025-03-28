using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;

namespace MachineLog.Collector.Extensions;

/// <summary>
/// ログ設定の拡張メソッドを提供するクラス
/// </summary>
public static class LoggingExtensions
{
  /// <summary>
  /// Serilogの設定を行います
  /// </summary>
  /// <param name="builder">ホストビルダー</param>
  /// <returns>設定済みのホストビルダー</returns>
  public static IHostBuilder UseCollectorLogging(this IHostBuilder builder)
  {
    return builder.UseSerilog((context, services, loggerConfiguration) =>
    {
      // Application Insights の接続文字列を取得
      var appInsightsConnectionString = context.Configuration["ApplicationInsights:ConnectionString"];
      var isDevelopment = context.HostingEnvironment.IsDevelopment();

      loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName();

      // 開発環境ではデバッグレベルのログを出力
      if (isDevelopment)
      {
        loggerConfiguration
          .MinimumLevel.Debug()
          .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
      }
      else
      {
        // 本番環境ではInformation以上のログを出力
        loggerConfiguration
          .MinimumLevel.Information()
          .WriteTo.Console(
            restrictedToMinimumLevel: LogEventLevel.Information,
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
      }

      // ファイルへのログ出力
      loggerConfiguration.WriteTo.File(
        path: $"logs/machinelog-collector-{context.HostingEnvironment.EnvironmentName}-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

      // Application Insightsが設定されている場合は出力
      if (!string.IsNullOrEmpty(appInsightsConnectionString))
      {
        loggerConfiguration.WriteTo.ApplicationInsights(
          appInsightsConnectionString,
          new TraceTelemetryConverter(),
          restrictedToMinimumLevel: LogEventLevel.Information);
      }
    });
  }
}
