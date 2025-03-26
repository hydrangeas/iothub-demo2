using AutoFixture;
using FluentAssertions;
using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using MachineLog.Collector.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.Services;

public class FileWatcherServiceTests : UnitTestBase
{
  private readonly Mock<ILogger<FileWatcherService>> _loggerMock;
  private readonly Mock<IOptions<CollectorConfig>> _optionsMock;
  private readonly CollectorConfig _config;
  private readonly string _testDirectory;

  public FileWatcherServiceTests(ITestOutputHelper output) : base(output)
  {
    _loggerMock = new Mock<ILogger<FileWatcherService>>();
    _config = new CollectorConfig
    {
      MonitoringPaths = new List<string>(),
      FileFilter = "*.jsonl",
      StabilizationPeriodSeconds = 1,
      FileExtensions = new List<string> { ".jsonl", ".log", ".json" },
      MaxDirectories = 10
    };
    _optionsMock = new Mock<IOptions<CollectorConfig>>();
    _optionsMock.Setup(x => x.Value).Returns(_config);

    // テスト用の一時ディレクトリを作成
    _testDirectory = Path.Combine(Path.GetTempPath(), $"FileWatcherTest_{Guid.NewGuid()}");
    Directory.CreateDirectory(_testDirectory);
    _config.MonitoringPaths.Add(_testDirectory);
  }

  public override void Dispose()
  {
    // テスト用ディレクトリの削除
    try
    {
      if (Directory.Exists(_testDirectory))
      {
        Directory.Delete(_testDirectory, true);
      }
    }
    catch (Exception ex)
    {
      Output.WriteLine($"テストディレクトリの削除中にエラーが発生しました: {ex.Message}");
    }

    base.Dispose();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task StartAsync_WithValidConfig_StartsWatching()
  {
    // Arrange
    var service = new FileWatcherService(_loggerMock.Object, _optionsMock.Object);

    // Act
    await service.StartAsync(CancellationToken.None);

    // Assert
    _loggerMock.Verify(
        x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("ファイル監視サービスを開始")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);

    // Clean up
    await service.StopAsync(CancellationToken.None);
    service.Dispose();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task StopAsync_AfterStarting_StopsWatching()
  {
    // Arrange
    var service = new FileWatcherService(_loggerMock.Object, _optionsMock.Object);
    await service.StartAsync(CancellationToken.None);

    // Act
    await service.StopAsync(CancellationToken.None);

    // Assert
    _loggerMock.Verify(
        x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("ファイル監視サービスを停止")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void AddWatchDirectory_WithValidPath_AddsDirectory()
  {
    // Arrange
    var service = new FileWatcherService(_loggerMock.Object, _optionsMock.Object);
    var newDirectory = Path.Combine(Path.GetTempPath(), $"FileWatcherTest2_{Guid.NewGuid()}");
    Directory.CreateDirectory(newDirectory);

    try
    {
      // Act
      var directoryId = service.AddWatchDirectory(newDirectory);

      // Assert
      directoryId.Should().NotBeEmpty("ディレクトリIDが返されるはずです");
      var directories = service.GetWatchDirectories();
      directories.Should().Contain(d => d.Path == newDirectory, "追加したディレクトリが監視リストに含まれるはずです");
    }
    finally
    {
      // Clean up
      if (Directory.Exists(newDirectory))
      {
        Directory.Delete(newDirectory, true);
      }
    }
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void RemoveWatchDirectory_WithValidId_RemovesDirectory()
  {
    // Arrange
    var service = new FileWatcherService(_loggerMock.Object, _optionsMock.Object);
    var newDirectory = Path.Combine(Path.GetTempPath(), $"FileWatcherTest2_{Guid.NewGuid()}");
    Directory.CreateDirectory(newDirectory);

    try
    {
      var directoryId = service.AddWatchDirectory(newDirectory);

      // Act
      var result = service.RemoveWatchDirectory(directoryId);

      // Assert
      result.Should().BeTrue("ディレクトリの削除に成功するはずです");
      var directories = service.GetWatchDirectories();
      directories.Should().NotContain(d => d.Path == newDirectory, "削除したディレクトリが監視リストに含まれないはずです");
    }
    finally
    {
      // Clean up
      if (Directory.Exists(newDirectory))
      {
        Directory.Delete(newDirectory, true);
      }
    }
  }
}