using System;
using System.Threading;
using System.Threading.Tasks;
using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using MachineLog.Collector.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.Services;

[Trait("Category", TestCategories.Unit)]
public class FileRetentionHostedServiceTests : UnitTestBase
{
  private readonly Mock<ILogger<FileRetentionHostedService>> _loggerMock;
  private readonly Mock<IOptions<CollectorConfig>> _optionsMock;
  private readonly Mock<IFileRetentionService> _fileRetentionServiceMock;
  private readonly CollectorConfig _config;
  private readonly FileRetentionHostedService _service;

  public FileRetentionHostedServiceTests(ITestOutputHelper output) : base(output)
  {
    _loggerMock = new Mock<ILogger<FileRetentionHostedService>>();
    _fileRetentionServiceMock = new Mock<IFileRetentionService>();
    _optionsMock = new Mock<IOptions<CollectorConfig>>();

    _config = new CollectorConfig
    {
      MonitoringPaths = new() { "/path1", "/path2" },
      DirectoryConfigs = new()
      {
        new DirectoryWatcherConfig { Path = "/path3" },
        new DirectoryWatcherConfig { Path = "/path4" }
      }
    };

    _optionsMock.Setup(o => o.Value).Returns(_config);
    _service = new FileRetentionHostedService(
      _loggerMock.Object,
      _optionsMock.Object,
      _fileRetentionServiceMock.Object);
  }

  [Fact]
  public async Task ExecuteAsync_監視ディレクトリのディスク容量をチェックすること()
  {
    // Arrange
    var stoppingToken = new CancellationTokenSource();

    // ディスク容量チェックの戻り値設定
    _fileRetentionServiceMock
      .Setup(s => s.CheckDiskSpaceAsync(It.IsAny<string>()))
      .ReturnsAsync(false);

    // Act - 非同期メソッドを開始して少し待機してからキャンセル
    var task = Task.Run(() => _service.StartAsync(stoppingToken.Token));

    // 少し待機して状態を確認
    await Task.Delay(100);

    // ディスク容量チェックが呼ばれたことを確認
    _fileRetentionServiceMock.Verify(
      s => s.CheckDiskSpaceAsync(It.IsAny<string>()),
      Times.AtLeastOnce);

    // キャンセルして終了
    stoppingToken.Cancel();
    await _service.StopAsync(CancellationToken.None);
  }

  [Fact]
  public async Task ExecuteAsync_ディスク容量不足時に緊急クリーンアップを実行すること()
  {
    // Arrange
    var stoppingToken = new CancellationTokenSource();

    // ディスク容量チェックの戻り値設定（容量不足）
    _fileRetentionServiceMock
      .Setup(s => s.CheckDiskSpaceAsync(It.IsAny<string>()))
      .ReturnsAsync(true);

    // Act - 非同期メソッドを開始して少し待機してからキャンセル
    var task = Task.Run(() => _service.StartAsync(stoppingToken.Token));

    // 少し待機して状態を確認
    await Task.Delay(100);

    // 緊急クリーンアップが呼ばれたことを確認
    _fileRetentionServiceMock.Verify(
      s => s.EmergencyCleanupAsync(It.IsAny<string>()),
      Times.AtLeastOnce);

    // キャンセルして終了
    stoppingToken.Cancel();
    await _service.StopAsync(CancellationToken.None);
  }

  [Fact]
  public async Task GetMonitoringDirectories_モニタリングパスとディレクトリ設定から監視対象を取得すること()
  {
    // Arrange - 設定済み
    var stoppingToken = new CancellationTokenSource();

    // Act - 非同期メソッドを開始して少し待機してからキャンセル
    var task = Task.Run(() => _service.StartAsync(stoppingToken.Token));

    // 少し待機して状態を確認
    await Task.Delay(100);

    // Assert - 設定された全てのディレクトリでディスク容量チェックが呼ばれること
    _fileRetentionServiceMock.Verify(s => s.CheckDiskSpaceAsync("/path1"), Times.AtLeastOnce);
    _fileRetentionServiceMock.Verify(s => s.CheckDiskSpaceAsync("/path2"), Times.AtLeastOnce);
    _fileRetentionServiceMock.Verify(s => s.CheckDiskSpaceAsync("/path3"), Times.AtLeastOnce);
    _fileRetentionServiceMock.Verify(s => s.CheckDiskSpaceAsync("/path4"), Times.AtLeastOnce);

    // キャンセルして終了
    stoppingToken.Cancel();
    await _service.StopAsync(CancellationToken.None);
  }
}
