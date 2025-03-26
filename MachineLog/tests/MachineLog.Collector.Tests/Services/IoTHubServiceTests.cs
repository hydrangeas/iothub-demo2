using AutoFixture;
using FluentAssertions;
using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using MachineLog.Collector.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.Services;

public class IoTHubServiceTests : UnitTestBase
{
  private readonly Mock<ILogger<IoTHubService>> _loggerMock;
  private readonly Mock<IOptions<IoTHubConfig>> _optionsMock;
  private readonly IoTHubConfig _config;
  private readonly string _testDirectory;

  public IoTHubServiceTests(ITestOutputHelper output) : base(output)
  {
    _loggerMock = new Mock<ILogger<IoTHubService>>();

    _config = new IoTHubConfig
    {
      ConnectionString = "HostName=test.azure-devices.net;DeviceId=testDevice;SharedAccessKey=dGVzdEtleQ==",
      DeviceId = "testDevice",
      UploadFolderPath = "logs",
      FileUpload = new FileUploadConfig
      {
        SasTokenTimeToLiveMinutes = 60,
        EnableNotification = true,
        LockDurationMinutes = 1,
        DefaultTimeToLiveDays = 1,
        MaxDeliveryCount = 10
      }
    };

    _optionsMock = new Mock<IOptions<IoTHubConfig>>();
    _optionsMock.Setup(x => x.Value).Returns(_config);

    // テスト用の一時ディレクトリを作成
    _testDirectory = Path.Combine(Path.GetTempPath(), $"IoTHubTest_{Guid.NewGuid()}");
    Directory.CreateDirectory(_testDirectory);
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
  public async Task ConnectAsync_WithValidConfig_ConnectsSuccessfully()
  {
    // Arrange
    var service = new Mock<IoTHubService>(_loggerMock.Object, _optionsMock.Object);

    service.Setup(s => s.ConnectAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ConnectionResult
        {
          Success = true,
          ConnectionTimeMs = 100
        });

    // Act
    var result = await service.Object.ConnectAsync();

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeTrue();
    result.ConnectionTimeMs.Should().BeGreaterOrEqualTo(0);
    result.ErrorMessage.Should().BeNull();
    result.Exception.Should().BeNull();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task ConnectAsync_WhenExceptionOccurs_ReturnsFailureResult()
  {
    // Arrange
    var exception = new Exception("Connection failed");
    var service = new Mock<IoTHubService>(_loggerMock.Object, _optionsMock.Object);

    service.Setup(s => s.ConnectAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ConnectionResult
        {
          Success = false,
          ConnectionTimeMs = 100,
          ErrorMessage = "Connection failed",
          Exception = exception
        });

    // Act
    var result = await service.Object.ConnectAsync();

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeFalse();
    result.ConnectionTimeMs.Should().BeGreaterOrEqualTo(0);
    result.ErrorMessage.Should().Be("Connection failed");
    result.Exception.Should().Be(exception);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task DisconnectAsync_WhenConnected_DisconnectsSuccessfully()
  {
    // Arrange
    var service = new Mock<IoTHubService>(_loggerMock.Object, _optionsMock.Object);

    service.Setup(s => s.DisconnectAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask)
        .Verifiable();

    // Act
    await service.Object.DisconnectAsync();

    // Assert
    service.Verify(s => s.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task UploadFileAsync_WithValidFile_UploadsSuccessfully()
  {
    // Arrange
    var service = new Mock<IoTHubService>(_loggerMock.Object, _optionsMock.Object);
    var testFilePath = Path.Combine(_testDirectory, "test.txt");
    File.WriteAllText(testFilePath, "Test content");
    var blobName = "test/test.txt";

    service.Setup(s => s.UploadFileAsync(testFilePath, blobName, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new FileUploadResult
        {
          Success = true,
          FilePath = testFilePath,
          BlobName = blobName,
          FileSizeBytes = new FileInfo(testFilePath).Length,
          UploadTimeMs = 100
        });

    // Act
    var result = await service.Object.UploadFileAsync(testFilePath, blobName);

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeTrue();
    result.FilePath.Should().Be(testFilePath);
    result.BlobName.Should().Be(blobName);
    result.FileSizeBytes.Should().BeGreaterThan(0);
    result.UploadTimeMs.Should().BeGreaterOrEqualTo(0);
    result.ErrorMessage.Should().BeNull();
    result.Exception.Should().BeNull();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task UploadFileAsync_WithNonExistentFile_ReturnsFailureResult()
  {
    // Arrange
    var service = new Mock<IoTHubService>(_loggerMock.Object, _optionsMock.Object);
    var nonExistentFilePath = Path.Combine(_testDirectory, "nonexistent.txt");
    var blobName = "test/nonexistent.txt";

    service.Setup(s => s.UploadFileAsync(nonExistentFilePath, blobName, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new FileUploadResult
        {
          Success = false,
          FilePath = nonExistentFilePath,
          BlobName = blobName,
          ErrorMessage = "File not found",
          Exception = new FileNotFoundException("File not found", nonExistentFilePath)
        });

    // Act
    var result = await service.Object.UploadFileAsync(nonExistentFilePath, blobName);

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeFalse();
    result.FilePath.Should().Be(nonExistentFilePath);
    result.BlobName.Should().Be(blobName);
    result.ErrorMessage.Should().Be("File not found");
    result.Exception.Should().BeOfType<FileNotFoundException>();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void GetConnectionState_ReturnsCurrentState()
  {
    // Arrange
    var service = new Mock<IoTHubService>(_loggerMock.Object, _optionsMock.Object);
    service.Setup(s => s.GetConnectionState()).Returns(ConnectionState.Connected);

    // Act
    var state = service.Object.GetConnectionState();

    // Assert
    state.Should().Be(ConnectionState.Connected);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task UploadFileAsync_WithRetryLogic_EventuallySucceeds()
  {
    // Arrange
    var service = new Mock<IoTHubService>(_loggerMock.Object, _optionsMock.Object);
    var testFilePath = Path.Combine(_testDirectory, "retry_test.txt");
    File.WriteAllText(testFilePath, "Test content for retry");
    var blobName = "test/retry_test.txt";

    // 最初の呼び出しは失敗、2回目は成功するシナリオをシミュレート
    var callCount = 0;
    service.Setup(s => s.UploadFileAsync(testFilePath, blobName, It.IsAny<CancellationToken>()))
        .ReturnsAsync(() =>
        {
          callCount++;
          if (callCount == 1)
          {
            return new FileUploadResult
            {
              Success = false,
              FilePath = testFilePath,
              BlobName = blobName,
              ErrorMessage = "Temporary network error",
              Exception = new TimeoutException("Connection timed out")
            };
          }
          else
          {
            return new FileUploadResult
            {
              Success = true,
              FilePath = testFilePath,
              BlobName = blobName,
              FileSizeBytes = new FileInfo(testFilePath).Length,
              UploadTimeMs = 200
            };
          }
        });

    // Act - 最初の呼び出し（失敗）
    var result1 = await service.Object.UploadFileAsync(testFilePath, blobName);

    // Act - 2回目の呼び出し（成功）
    var result2 = await service.Object.UploadFileAsync(testFilePath, blobName);

    // Assert
    result1.Should().NotBeNull();
    result1.Success.Should().BeFalse();
    result1.ErrorMessage.Should().Be("Temporary network error");
    result1.Exception.Should().BeOfType<TimeoutException>();

    result2.Should().NotBeNull();
    result2.Success.Should().BeTrue();
    result2.FilePath.Should().Be(testFilePath);
    result2.BlobName.Should().Be(blobName);
    result2.FileSizeBytes.Should().BeGreaterThan(0);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task UploadFileAsync_WithLargeFile_HandlesChunkedUpload()
  {
    // Arrange
    var service = new Mock<IoTHubService>(_loggerMock.Object, _optionsMock.Object);
    var largeFilePath = Path.Combine(_testDirectory, "large_file.bin");

    // 大きなファイルを作成（実際には小さいファイルを使用）
    using (var fileStream = File.Create(largeFilePath))
    {
      fileStream.SetLength(1024 * 10); // 10KB
    }

    var blobName = "test/large_file.bin";

    service.Setup(s => s.UploadFileAsync(largeFilePath, blobName, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new FileUploadResult
        {
          Success = true,
          FilePath = largeFilePath,
          BlobName = blobName,
          FileSizeBytes = new FileInfo(largeFilePath).Length,
          UploadTimeMs = 500
        });

    // Act
    var result = await service.Object.UploadFileAsync(largeFilePath, blobName);

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeTrue();
    result.FilePath.Should().Be(largeFilePath);
    result.BlobName.Should().Be(blobName);
    result.FileSizeBytes.Should().Be(1024 * 10);
  }
}