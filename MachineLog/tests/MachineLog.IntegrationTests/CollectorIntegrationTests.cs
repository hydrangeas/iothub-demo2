using MachineLog.Collector.Extensions;
using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using MachineLog.Common.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;
using Xunit;

namespace MachineLog.IntegrationTests;

public class CollectorIntegrationTests : IDisposable
{
  private readonly IHost _host;
  private readonly string _monitoringPath;
  private readonly string _archivePath;
  private readonly Mock<IIoTHubService> _mockIoTHubService;
  private readonly List<(string FilePath, string BlobName)> _uploadedFiles = new();

  public CollectorIntegrationTests()
  {
    // 一時ディレクトリを作成
    _monitoringPath = Path.Combine(Path.GetTempPath(), "CollectorIntegrationTests_Monitor_" + Guid.NewGuid().ToString("N"));
    _archivePath = Path.Combine(Path.GetTempPath(), "CollectorIntegrationTests_Archive_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_monitoringPath);
    Directory.CreateDirectory(_archivePath);

    // モックの設定
    _mockIoTHubService = new Mock<IIoTHubService>();
    _mockIoTHubService.Setup(s => s.ConnectAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ConnectionResult { Success = true });
    _mockIoTHubService.Setup(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .Callback<string, string, CancellationToken>((fp, bn, ct) => _uploadedFiles.Add((fp, bn)))
                      .ReturnsAsync((string fp, string bn, CancellationToken ct) => new FileUploadResult
                      {
                        Success = true,
                        FilePath = fp,
                        BlobName = bn,
                        FileSizeBytes = new FileInfo(fp).Length // Simulate size
                      });

    // 設定の構成
    var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
              [$"{nameof(CollectorConfig)}:{nameof(CollectorConfig.MonitoringPaths)}:0"] = _monitoringPath,
              [$"{nameof(CollectorConfig)}:{nameof(CollectorConfig.RetentionPolicy)}:{nameof(RetentionPolicy.ArchiveDirectoryPath)}"] = _archivePath, // 修正
              [$"{nameof(CollectorConfig)}:{nameof(CollectorConfig.StabilizationPeriodSeconds)}"] = "1", // 短い安定期間
              [$"{nameof(CollectorConfig)}:{nameof(CollectorConfig.FileExtensions)}:0"] = ".log",
              [$"{nameof(BatchConfig)}:{nameof(BatchConfig.MaxBatchItems)}"] = "10", // 小さなバッチサイズ
              [$"{nameof(BatchConfig)}:{nameof(BatchConfig.MaxBatchSizeBytes)}"] = "1024", // 小さなバッチサイズ
              [$"{nameof(BatchConfig)}:{nameof(BatchConfig.ProcessingIntervalSeconds)}"] = "1", // 短い処理間隔
              [$"{nameof(IoTHubConfig)}:{nameof(IoTHubConfig.DeviceId)}"] = "TestDevice",
              // 他の必要な設定があれば追加
            })
        .Build();

    // DIコンテナとホストの構築
    _host = Host.CreateDefaultBuilder()
        .ConfigureLogging(logging => logging.ClearProviders().AddDebug()) // 必要に応じてロギングを追加
        .ConfigureServices((context, services) =>
        {
          services.AddCollectorConfiguration(configuration); // 拡張メソッドを使用
          services.AddCollectorServices(); // 拡張メソッドを使用

          // IIoTHubServiceをモックに置き換え
          services.Remove(services.First(d => d.ServiceType == typeof(IIoTHubService)));
          services.AddSingleton(_mockIoTHubService.Object);

          // FileRetentionHostedService は長時間実行されるため、テストでは無効化するか調整が必要な場合がある
          // 今回は単純化のためそのままにするが、必要に応じてモック化や設定変更を検討
        })
        .Build();
  }

  [Fact]
  public async Task FileProcessing_EndToEnd_Success()
  {
    // Arrange
    var fileWatcherService = _host.Services.GetRequiredService<IFileWatcherService>();
    var batchProcessorService = _host.Services.GetRequiredService<IBatchProcessorService>();

    // テスト用ログファイルの内容
    var logContent = @"{""Id"":""log1"",""Timestamp"":""2025-03-28T10:00:00Z"",""Level"":""Info"",""Message"":""Test message 1""}" + Environment.NewLine +
                     @"{""Id"":""log2"",""Timestamp"":""2025-03-28T10:00:01Z"",""Level"":""Warn"",""Message"":""Test message 2""}";
    var testLogFileName = "test.log";
    var testLogFilePath = Path.Combine(_monitoringPath, testLogFileName);

    // Act
    // サービスの開始 (ホストを開始する前に手動で開始するか、ホストのライフサイクルに任せる)
    // ここではホストの開始に任せる
    await _host.StartAsync();

    // ログファイルを監視ディレクトリに書き込む
    await File.WriteAllTextAsync(testLogFilePath, logContent, Encoding.UTF8);

    // ファイルが処理され、アップロードされるのを待機
    // StabilizationPeriod + ProcessingInterval + α の時間待機
    await Task.Delay(TimeSpan.FromSeconds(5));

    // 強制的にバッチ処理を実行して残りを処理
    await batchProcessorService.ProcessBatchAsync(true);
    await Task.Delay(TimeSpan.FromSeconds(1)); // アップロード完了待ち

    // Assert
    // アップロードが1回呼び出されたことを確認 (バッチ処理のため1ファイル=1アップロードとは限らない)
    Assert.NotEmpty(_uploadedFiles);

    // アップロードされたファイル名が期待通りか確認 (Blob名は実装依存)
    // ここでは単純に元のファイル名が含まれているかチェック
    Assert.Contains(_uploadedFiles, uf => uf.BlobName.Contains(testLogFileName));

    // 元のファイルがアーカイブされているか確認 (FileRetentionServiceの動作)
    // FileRetentionServiceが有効な場合、アーカイブパスにファイルが存在するはず
    // Assert.True(Directory.EnumerateFiles(_archivePath, "*.gz").Any(f => f.Contains(testLogFileName)));
    // 注意: FileRetentionServiceのテストは別途行うか、より詳細な設定が必要

    // Stop host
    await _host.StopAsync();
  }

  public void Dispose()
  {
    _host?.Dispose();

    // 一時ディレクトリのクリーンアップ
    try { Directory.Delete(_monitoringPath, true); } catch { /* ignore */ }
    try { Directory.Delete(_archivePath, true); } catch { /* ignore */ }
  }
}
