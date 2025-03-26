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
      StabilizationPeriodSeconds = 1
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
  public async Task FileCreated_WhenFileIsCreated_RaisesEvent()
  {
    // Arrange
    var service = new FileWatcherService(_loggerMock.Object, _optionsMock.Object);
    var eventRaised = false;
    var eventPath = string.Empty;

    service.FileCreated += (sender, e) =>
    {
      eventRaised = true;
      eventPath = e.FullPath;
    };

    await service.StartAsync(CancellationToken.None);

    // テスト用のファイルを作成する前に少し待機
    await Task.Delay(500);

    // Act
    var testFilePath = Path.Combine(_testDirectory, $"test_{Guid.NewGuid()}.jsonl");
    File.WriteAllText(testFilePath, "test content");

    // ファイル作成イベントが発生するまで待機
    var timeout = TimeSpan.FromSeconds(5);
    var startTime = DateTime.UtcNow;
    while (!eventRaised && DateTime.UtcNow - startTime < timeout)
    {
      await Task.Delay(100);
    }

    // Assert
    eventRaised.Should().BeTrue("ファイル作成イベントが発生するはずです");
    eventPath.Should().Be(testFilePath);

    // Clean up
    await service.StopAsync(CancellationToken.None);
    service.Dispose();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task FileChanged_WhenFileIsModified_RaisesEvent()
  {
    // Arrange
    var service = new FileWatcherService(_loggerMock.Object, _optionsMock.Object);
    var eventRaised = false;
    var eventPath = string.Empty;

    service.FileChanged += (sender, e) =>
    {
      eventRaised = true;
      eventPath = e.FullPath;
    };

    // テスト用のファイルを事前に作成
    var testFilePath = Path.Combine(_testDirectory, $"test_{Guid.NewGuid()}.jsonl");
    File.WriteAllText(testFilePath, "initial content");

    await service.StartAsync(CancellationToken.None);

    // テスト用のファイルを変更する前に少し待機
    await Task.Delay(500);

    // Act
    File.AppendAllText(testFilePath, "\nadditional content");

    // ファイル変更イベントが発生するまで待機
    var timeout = TimeSpan.FromSeconds(5);
    var startTime = DateTime.UtcNow;
    while (!eventRaised && DateTime.UtcNow - startTime < timeout)
    {
      await Task.Delay(100);
    }

    // Assert
    eventRaised.Should().BeTrue("ファイル変更イベントが発生するはずです");
    eventPath.Should().Be(testFilePath);

    // Clean up
    await service.StopAsync(CancellationToken.None);
    service.Dispose();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task FileStabilized_WhenFileIsStable_RaisesEvent()
  {
    // Arrange
    var service = new FileWatcherService(_loggerMock.Object, _optionsMock.Object);
    var eventRaised = false;
    var eventPath = string.Empty;

    service.FileStabilized += (sender, e) =>
    {
      eventRaised = true;
      eventPath = e.FullPath;
    };

    await service.StartAsync(CancellationToken.None);

    // テスト用のファイルを作成する前に少し待機
    await Task.Delay(500);

    // Act
    var testFilePath = Path.Combine(_testDirectory, $"test_{Guid.NewGuid()}.jsonl");
    File.WriteAllText(testFilePath, "test content");

    // ファイル安定化イベントが発生するまで待機（安定化期間 + 余裕）
    var timeout = TimeSpan.FromSeconds(_config.StabilizationPeriodSeconds + 3);
    var startTime = DateTime.UtcNow;
    while (!eventRaised && DateTime.UtcNow - startTime < timeout)
    {
      await Task.Delay(100);
    }

    // Assert
    eventRaised.Should().BeTrue("ファイル安定化イベントが発生するはずです");
    Path.GetFullPath(eventPath).Should().Be(Path.GetFullPath(testFilePath));

    // Clean up
    await service.StopAsync(CancellationToken.None);
    service.Dispose();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task StartAsync_WithMultipleDirectories_WatchesAllDirectories()
  {
    // Arrange
    var secondTestDirectory = Path.Combine(Path.GetTempPath(), $"FileWatcherTest2_{Guid.NewGuid()}");
    Directory.CreateDirectory(secondTestDirectory);
    _config.MonitoringPaths.Add(secondTestDirectory);

    var service = new FileWatcherService(_loggerMock.Object, _optionsMock.Object);
    var firstDirEventRaised = false;
    var secondDirEventRaised = false;
    var eventPath = string.Empty;

    service.FileCreated += (sender, e) =>
    {
      eventPath = e.FullPath;
      if (eventPath.StartsWith(_testDirectory))
      {
        firstDirEventRaised = true;
      }
      else if (eventPath.StartsWith(secondTestDirectory))
      {
        secondDirEventRaised = true;
      }
    };

    await service.StartAsync(CancellationToken.None);

    // テスト用のファイルを作成する前に少し待機
    await Task.Delay(500);

    // Act - 最初のディレクトリにファイルを作成
    var firstFilePath = Path.Combine(_testDirectory, $"test1_{Guid.NewGuid()}.jsonl");
    File.WriteAllText(firstFilePath, "test content 1");

    // 最初のイベントが発生するまで待機
    var timeout = TimeSpan.FromSeconds(5);
    var startTime = DateTime.UtcNow;
    while (!firstDirEventRaised && DateTime.UtcNow - startTime < timeout)
    {
      await Task.Delay(100);
    }

    // 2番目のディレクトリにファイルを作成
    var secondFilePath = Path.Combine(secondTestDirectory, $"test2_{Guid.NewGuid()}.jsonl");
    File.WriteAllText(secondFilePath, "test content 2");

    // 2番目のイベントが発生するまで待機
    startTime = DateTime.UtcNow;
    while (!secondDirEventRaised && DateTime.UtcNow - startTime < timeout)
    {
      await Task.Delay(100);
    }

    // Assert
    firstDirEventRaised.Should().BeTrue("最初のディレクトリのファイル作成イベントが発生するはずです");
    secondDirEventRaised.Should().BeTrue("2番目のディレクトリのファイル作成イベントが発生するはずです");

    // Clean up
    await service.StopAsync(CancellationToken.None);
    service.Dispose();
    try
    {
      if (Directory.Exists(secondTestDirectory))
      {
        Directory.Delete(secondTestDirectory, true);
      }
    }
    catch (Exception ex)
    {
      Output.WriteLine($"2番目のテストディレクトリの削除中にエラーが発生しました: {ex.Message}");
    }
  }
}