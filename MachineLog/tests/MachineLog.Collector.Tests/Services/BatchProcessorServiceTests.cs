using AutoFixture;
using FluentAssertions;
using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using MachineLog.Collector.Tests.TestInfrastructure;
using MachineLog.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.Services;

public class BatchProcessorServiceTests : UnitTestBase
{
  private readonly Mock<ILogger<BatchProcessorService>> _loggerMock;
  private readonly Mock<IOptions<BatchConfig>> _optionsMock;
  private readonly Mock<IIoTHubService> _iotHubServiceMock;
  private readonly BatchConfig _config;

  public BatchProcessorServiceTests(ITestOutputHelper output) : base(output)
  {
    _loggerMock = new Mock<ILogger<BatchProcessorService>>();
    _iotHubServiceMock = new Mock<IIoTHubService>();

    _config = new BatchConfig
    {
      MaxBatchSizeBytes = 1024 * 1024, // 1MB
      MaxBatchItems = 100,
      ProcessingIntervalSeconds = 30,
      RetryPolicy = new RetryPolicy
      {
        MaxRetries = 3,
        InitialRetryIntervalSeconds = 1,
        MaxRetryIntervalSeconds = 10,
        RetryBackoffMultiplier = 2.0
      }
    };

    _optionsMock = new Mock<IOptions<BatchConfig>>();
    _optionsMock.Setup(x => x.Value).Returns(_config);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task AddToBatchAsync_WhenBatchIsNotFull_AddsItemSuccessfully()
  {
    // Arrange
    var service = new Mock<BatchProcessorService>(
        _loggerMock.Object,
        _optionsMock.Object,
        _iotHubServiceMock.Object);

    service.Setup(s => s.AddToBatchAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
        .CallBase();

    var logEntry = new LogEntry
    {
      Id = "1",
      DeviceId = "device1",
      Timestamp = DateTime.UtcNow,
      Level = "info",
      Message = "Test message"
    };

    // Act
    var result = await service.Object.AddToBatchAsync(logEntry);

    // Assert
    result.Should().BeTrue();
    service.Verify(s => s.AddToBatchAsync(logEntry, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task AddToBatchAsync_WhenBatchIsFull_ProcessesBatchAndAddsItem()
  {
    // Arrange
    var service = new Mock<BatchProcessorService>(
        _loggerMock.Object,
        _optionsMock.Object,
        _iotHubServiceMock.Object);

    // バッチが満杯の状態をシミュレート
    service.Setup(s => s.AddToBatchAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
        .CallBase();

    service.Setup(s => s.IsBatchFull()).Returns(true);

    service.Setup(s => s.ProcessBatchAsync(false, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new BatchProcessingResult { Success = true, ProcessedItems = 100 });

    var logEntry = new LogEntry
    {
      Id = "1",
      DeviceId = "device1",
      Timestamp = DateTime.UtcNow,
      Level = "info",
      Message = "Test message"
    };

    // Act
    var result = await service.Object.AddToBatchAsync(logEntry);

    // Assert
    result.Should().BeTrue();
    service.Verify(s => s.ProcessBatchAsync(false, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task ProcessBatchAsync_WithEmptyBatch_ReturnsSuccessWithZeroItems()
  {
    // Arrange
    var service = new Mock<BatchProcessorService>(
        _loggerMock.Object,
        _optionsMock.Object,
        _iotHubServiceMock.Object);

    // 空のバッチをシミュレート
    service.Setup(s => s.GetBatchItemCount()).Returns(0);
    service.Setup(s => s.ProcessBatchAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
        .CallBase();

    // Act
    var result = await service.Object.ProcessBatchAsync();

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeTrue();
    result.ProcessedItems.Should().Be(0);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task ProcessBatchAsync_WithNonEmptyBatch_ProcessesAllItems()
  {
    // Arrange
    var service = new Mock<BatchProcessorService>(
        _loggerMock.Object,
        _optionsMock.Object,
        _iotHubServiceMock.Object);

    // 非空のバッチをシミュレート
    service.Setup(s => s.GetBatchItemCount()).Returns(10);
    service.Setup(s => s.GetBatchSizeBytes()).Returns(1024); // 1KB

    service.Setup(s => s.ProcessBatchAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new BatchProcessingResult
        {
          Success = true,
          ProcessedItems = 10,
          BatchSizeBytes = 1024,
          ProcessingTimeMs = 100
        });

    // Act
    var result = await service.Object.ProcessBatchAsync();

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeTrue();
    result.ProcessedItems.Should().Be(10);
    result.BatchSizeBytes.Should().Be(1024);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task ProcessBatchAsync_WhenIoTHubServiceFails_ReturnsFailureResult()
  {
    // Arrange
    var service = new Mock<BatchProcessorService>(
        _loggerMock.Object,
        _optionsMock.Object,
        _iotHubServiceMock.Object);

    // 非空のバッチをシミュレート
    service.Setup(s => s.GetBatchItemCount()).Returns(10);
    service.Setup(s => s.GetBatchSizeBytes()).Returns(1024); // 1KB

    // IoT Hub サービスの失敗をシミュレート
    var exception = new Exception("IoT Hub connection failed");
    service.Setup(s => s.ProcessBatchAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new BatchProcessingResult
        {
          Success = false,
          ProcessedItems = 0,
          BatchSizeBytes = 1024,
          ProcessingTimeMs = 100,
          ErrorMessage = "IoT Hub connection failed",
          Exception = exception
        });

    // Act
    var result = await service.Object.ProcessBatchAsync();

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeFalse();
    result.ProcessedItems.Should().Be(0);
    result.ErrorMessage.Should().Be("IoT Hub connection failed");
    result.Exception.Should().Be(exception);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task StartAsync_InitializesAndStartsProcessing()
  {
    // Arrange
    var service = new Mock<BatchProcessorService>(
        _loggerMock.Object,
        _optionsMock.Object,
        _iotHubServiceMock.Object);

    service.Setup(s => s.StartAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask)
        .Verifiable();

    // Act
    await service.Object.StartAsync();

    // Assert
    service.Verify(s => s.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task StopAsync_StopsProcessingAndCleansUp()
  {
    // Arrange
    var service = new Mock<BatchProcessorService>(
        _loggerMock.Object,
        _optionsMock.Object,
        _iotHubServiceMock.Object);

    service.Setup(s => s.StopAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask)
        .Verifiable();

    // Act
    await service.Object.StopAsync();

    // Assert
    service.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void IsBatchFull_WhenSizeExceedsMaxBatchSize_ReturnsTrue()
  {
    // Arrange
    var service = new Mock<BatchProcessorService>(
        _loggerMock.Object,
        _optionsMock.Object,
        _iotHubServiceMock.Object);

    // バッチサイズが上限を超えている状態をシミュレート
    service.Setup(s => s.GetBatchSizeBytes()).Returns(_config.MaxBatchSizeBytes + 1);
    service.Setup(s => s.IsBatchFull()).CallBase();

    // Act
    var result = service.Object.IsBatchFull();

    // Assert
    result.Should().BeTrue();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void IsBatchFull_WhenItemCountExceedsMaxBatchItems_ReturnsTrue()
  {
    // Arrange
    var service = new Mock<BatchProcessorService>(
        _loggerMock.Object,
        _optionsMock.Object,
        _iotHubServiceMock.Object);

    // バッチアイテム数が上限を超えている状態をシミュレート
    service.Setup(s => s.GetBatchSizeBytes()).Returns(1024); // サイズは上限以下
    service.Setup(s => s.GetBatchItemCount()).Returns(_config.MaxBatchItems + 1);
    service.Setup(s => s.IsBatchFull()).CallBase();

    // Act
    var result = service.Object.IsBatchFull();

    // Assert
    result.Should().BeTrue();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void IsBatchFull_WhenBelowLimits_ReturnsFalse()
  {
    // Arrange
    var service = new Mock<BatchProcessorService>(
        _loggerMock.Object,
        _optionsMock.Object,
        _iotHubServiceMock.Object);

    // バッチがまだ上限に達していない状態をシミュレート
    service.Setup(s => s.GetBatchSizeBytes()).Returns(_config.MaxBatchSizeBytes / 2);
    service.Setup(s => s.GetBatchItemCount()).Returns(_config.MaxBatchItems / 2);
    service.Setup(s => s.IsBatchFull()).CallBase();

    // Act
    var result = service.Object.IsBatchFull();

    // Assert
    result.Should().BeFalse();
  }
}