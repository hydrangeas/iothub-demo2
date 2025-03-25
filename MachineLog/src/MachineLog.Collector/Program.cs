using Microsoft.Extensions.Hosting;
using MachineLog.Collector.Extensions;

var builder = Host.CreateDefaultBuilder(args);

builder
    .UseCollectorLogging()
    .ConfigureServices((context, services) =>
    {
      // 設定の登録
      services.AddCollectorConfiguration(context.Configuration);

      // サービスの登録
      services.AddCollectorServices();
    });

var host = builder.Build();
await host.RunAsync();
