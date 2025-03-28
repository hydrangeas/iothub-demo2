using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MachineLog.Collector.Extensions;
using Azure.Identity; // Azure Key Vault 認証用に追加
using Microsoft.Extensions.Configuration; // AddAzureKeyVault 用に追加

var builder = WebApplication.CreateBuilder(args);

// Azure Key Vault の設定を追加
var keyVaultUri = builder.Configuration["KeyVaultUri"];
if (!string.IsNullOrEmpty(keyVaultUri) && Uri.TryCreate(keyVaultUri, UriKind.Absolute, out var validUri))
{
    builder.Configuration.AddAzureKeyVault(validUri, new DefaultAzureCredential());
}

// ログ設定
builder.Host.UseCollectorLogging();

// サービス設定
// 設定の登録
builder.Services.AddCollectorConfiguration(builder.Configuration);
// サービスの登録
builder.Services.AddCollectorServices();

var app = builder.Build();

app.UseRouting();

app.UseEndpoints(endpoints =>
{
    // ヘルスチェックエンドポイントの登録
    endpoints.MapHealthChecks("/health");

    // 詳細なヘルスチェックエンドポイントの登録
    endpoints.MapHealthChecks("/health/detail", new HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";

            var result = new
            {
                status = report.Status.ToString(),
                totalDuration = report.TotalDuration.TotalMilliseconds,
                entries = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds,
                    data = e.Value.Data
                })
            };

            await System.Text.Json.JsonSerializer.SerializeAsync(
                context.Response.Body,
                result,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    });
});

await app.RunAsync();
