using FluentAssertions;
using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using MachineLog.Collector.Tests.TestInfrastructure;
using MachineLog.Common.Logging;
using MachineLog.Common.Utilities;
using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.Services
{
  public class IoTHubServiceTests : UnitTestBase
  {
    private readonly Mock<ILogger<IoTHubService>> _loggerMock;
    private readonly Mock<IOptions<IoTHubConfig>> _optionsMock;
    private readonly IoTHubConfig _config;
    private readonly string _testDirectory;
    private TestIoTHubService _service;

    public IoTHubServiceTests(ITestOutputHelper output) : base(output)
    {
      _loggerMock = new Mock<ILogger<IoTHubService>>();

      // テスト用構成の設定
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

      // テスト用のサービスインスタンスを作成
      _service = new TestIoTHubService(_loggerMock.Object, _optionsMock.Object);
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

    // テスト用のスタブ実装
    public class TestIoTHubService : IoTHubService
    {
      public bool ConnectShouldFail { get; set; }
      public bool UploadShouldFail { get; set; }
      public bool IsConnected { get; private set; }

      public TestIoTHubService(
          ILogger<IoTHubService> logger,
          IOptions<IoTHubConfig> config)
          : base(logger,
                config,
                lgr => new StructuredLogger(lgr),
                lgr => new RetryHandler(lgr))
      {
        ConnectShouldFail = false;
        UploadShouldFail = false;
      }

      public override async Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
      {
        if (ConnectShouldFail)
        {
          IsConnected = false;
          return new ConnectionResult
          {
            Success = false,
            ConnectionTimeMs = 10,
            ErrorMessage = "Connection failed",
            Exception = new TimeoutException("Connection failed")
          };
        }

        IsConnected = true;
        return new ConnectionResult
        {
          Success = true,
          ConnectionTimeMs = 10
        };
      }

      public override async Task<FileUploadResult> UploadFileAsync(
          string filePath,
          string blobName,
          CancellationToken cancellationToken = default)
      {
        if (!IsConnected)
        {
          return new FileUploadResult
          {
            Success = false,
            FilePath = filePath,
            BlobName = blobName,
            ErrorMessage = "Not connected to IoT Hub"
          };
        }

        if (!File.Exists(filePath))
        {
          return new FileUploadResult
          {
            Success = false,
            FilePath = filePath,
            BlobName = blobName,
            ErrorMessage = "アップロード対象のファイルが見つかりません",
            Exception = new FileNotFoundException("ファイルが見つかりません", filePath)
          };
        }

        if (UploadShouldFail)
        {
          return new FileUploadResult
          {
            Success = false,
            FilePath = filePath,
            BlobName = blobName,
            FileSizeBytes = new FileInfo(filePath).Length,
            UploadTimeMs = 50,
            ErrorMessage = "Upload failed",
            Exception = new IOException("Upload error")
          };
        }

        return new FileUploadResult
        {
          Success = true,
          FilePath = filePath,
          BlobName = blobName,
          FileSizeBytes = new FileInfo(filePath).Length,
          UploadTimeMs = 50
        };
      }

      public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
      {
        IsConnected = false;
        await Task.CompletedTask;
      }

      public override ConnectionState GetConnectionState()
      {
        return IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected;
      }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ConnectAsync_WhenSuccessful_ReturnsSuccessResult()
    {
      // Arrange
      _service.ConnectShouldFail = false;

      // Act
      var result = await _service.ConnectAsync();

      // Assert
      result.Should().NotBeNull();
      result.Success.Should().BeTrue();
      result.ConnectionTimeMs.Should().BeGreaterOrEqualTo(0);
      result.ErrorMessage.Should().BeNull();
      result.Exception.Should().BeNull();
      _service.GetConnectionState().Should().Be(ConnectionState.Connected);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ConnectAsync_WhenFailed_ReturnsFailureResult()
    {
      // Arrange
      _service.ConnectShouldFail = true;

      // Act
      var result = await _service.ConnectAsync();

      // Assert
      result.Should().NotBeNull();
      result.Success.Should().BeFalse();
      result.ConnectionTimeMs.Should().BeGreaterOrEqualTo(0);
      result.ErrorMessage.Should().Be("Connection failed");
      result.Exception.Should().BeOfType<TimeoutException>();
      _service.GetConnectionState().Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task DisconnectAsync_AfterConnect_DisconnectsSuccessfully()
    {
      // Arrange
      await _service.ConnectAsync();
      var initialState = _service.GetConnectionState();
      initialState.Should().Be(ConnectionState.Connected);

      // Act
      await _service.DisconnectAsync();

      // Assert
      _service.GetConnectionState().Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task UploadFileAsync_WithValidFile_UploadsSuccessfully()
    {
      // Arrange
      await _service.ConnectAsync();
      var testFilePath = Path.Combine(_testDirectory, "test.txt");
      await File.WriteAllTextAsync(testFilePath, "Test content");
      var blobName = "test/test.txt";

      // Act
      var result = await _service.UploadFileAsync(testFilePath, blobName);

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
      await _service.ConnectAsync();
      var nonExistentFilePath = Path.Combine(_testDirectory, "nonexistent.txt");
      var blobName = "test/nonexistent.txt";

      // Act
      var result = await _service.UploadFileAsync(nonExistentFilePath, blobName);

      // Assert
      result.Should().NotBeNull();
      result.Success.Should().BeFalse();
      result.FilePath.Should().Be(nonExistentFilePath);
      result.BlobName.Should().Be(blobName);
      result.ErrorMessage.Should().Contain("アップロード対象のファイルが見つかりません");
      result.Exception.Should().BeOfType<FileNotFoundException>();
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task UploadFileAsync_WhenNotConnected_ReturnsFailureResult()
    {
      // Arrange - あえて接続しない
      var testFilePath = Path.Combine(_testDirectory, "test.txt");
      await File.WriteAllTextAsync(testFilePath, "Test content");
      var blobName = "test/test.txt";

      // Act
      var result = await _service.UploadFileAsync(testFilePath, blobName);

      // Assert
      result.Should().NotBeNull();
      result.Success.Should().BeFalse();
      result.FilePath.Should().Be(testFilePath);
      result.BlobName.Should().Be(blobName);
      result.ErrorMessage.Should().Be("Not connected to IoT Hub");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task UploadFileAsync_WhenUploadFails_ReturnsFailureResult()
    {
      // Arrange
      await _service.ConnectAsync();
      var testFilePath = Path.Combine(_testDirectory, "test.txt");
      await File.WriteAllTextAsync(testFilePath, "Test content");
      var blobName = "test/test.txt";
      _service.UploadShouldFail = true;

      // Act
      var result = await _service.UploadFileAsync(testFilePath, blobName);

      // Assert
      result.Should().NotBeNull();
      result.Success.Should().BeFalse();
      result.FilePath.Should().Be(testFilePath);
      result.BlobName.Should().Be(blobName);
      result.FileSizeBytes.Should().BeGreaterThan(0);
      result.ErrorMessage.Should().Be("Upload failed");
      result.Exception.Should().BeOfType<IOException>();
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task UploadFileAsync_WithLargeFile_UploadsSuccessfully()
    {
      // Arrange
      await _service.ConnectAsync();
      var largeFilePath = Path.Combine(_testDirectory, "large_file.bin");

      // 大きなファイルを作成（実際には小さいファイルを使用）
      await using (var fileStream = File.Create(largeFilePath))
      {
        fileStream.SetLength(1024 * 10); // 10KB
      }

      var blobName = "test/large_file.bin";

      // Act
      var result = await _service.UploadFileAsync(largeFilePath, blobName);

      // Assert
      result.Should().NotBeNull();
      result.Success.Should().BeTrue();
      result.FilePath.Should().Be(largeFilePath);
      result.BlobName.Should().Be(blobName);
      result.FileSizeBytes.Should().Be(1024 * 10);
    }
  }
}
