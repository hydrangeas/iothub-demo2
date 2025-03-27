using AutoFixture;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using MachineLog.Collector.Tests.TestInfrastructure;
using MachineLog.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.Services;

public class FileProcessorServiceTests : UnitTestBase
{
  private readonly Mock<ILogger<FileProcessorService>> _loggerMock;
  private readonly Mock<IOptions<CollectorConfig>> _optionsMock;
  private readonly Mock<IValidator<LogEntry>> _validatorMock;
  private readonly Mock<JsonLineProcessor> _jsonProcessorMock;
  private readonly Mock<EncodingDetector> _encodingDetectorMock;
  private readonly CollectorConfig _config;
  private readonly string _testDirectory;

  public FileProcessorServiceTests(ITestOutputHelper output) : base(output)
  {
    _loggerMock = new Mock<ILogger<FileProcessorService>>();
    _validatorMock = new Mock<IValidator<LogEntry>>();
    _jsonProcessorMock = new Mock<JsonLineProcessor>();
    _encodingDetectorMock = new Mock<EncodingDetector>();

    _config = new CollectorConfig
    {
      FileFilter = "*.jsonl",
      RetentionPolicy = new RetentionPolicy
      {
        LargeFileSizeThreshold = 10 * 1024 * 1024 // 10MB
      }
    };

    _optionsMock = new Mock<IOptions<CollectorConfig>>();
    _optionsMock.Setup(x => x.Value).Returns(_config);

    // テスト用の一時ディレクトリを作成
    _testDirectory = Path.Combine(Path.GetTempPath(), $"FileProcessorTest_{Guid.NewGuid()}");
    Directory.CreateDirectory(_testDirectory);

    // デフォルトのバリデーション設定
    _validatorMock
        .Setup(v => v.ValidateAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ValidationResult());
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
  public async Task ProcessFileAsync_WithValidJsonLines_ProcessesSuccessfully()
  {
    // Arrange
    var service = new FileProcessorService(
        _loggerMock.Object,
        _optionsMock.Object,
        _validatorMock.Object,
        _jsonProcessorMock.Object,
        _encodingDetectorMock.Object);
    var testFilePath = Path.Combine(_testDirectory, "valid.jsonl");

    // 有効なJSONLinesファイルを作成
    var validEntries = new List<string>
        {
            JsonSerializer.Serialize(new LogEntry { Id = "1", DeviceId = "device1", Timestamp = DateTime.UtcNow, Level = "info", Message = "Test message 1" }),
            JsonSerializer.Serialize(new LogEntry { Id = "2", DeviceId = "device2", Timestamp = DateTime.UtcNow, Level = "error", Message = "Test message 2" })
        };

    await File.WriteAllLinesAsync(testFilePath, validEntries);

    // Act
    var result = await service.ProcessFileAsync(testFilePath);

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeTrue();
    result.ProcessedRecords.Should().Be(2);
    result.ErrorMessage.Should().BeNull();
    result.Exception.Should().BeNull();

    _validatorMock.Verify(
        v => v.ValidateAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()),
        Times.Exactly(2));
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task ProcessFileAsync_WithInvalidJson_ReturnsPartialSuccess()
  {
    // Arrange
    var service = new FileProcessorService(
        _loggerMock.Object,
        _optionsMock.Object,
        _validatorMock.Object,
        _jsonProcessorMock.Object,
        _encodingDetectorMock.Object);
    var testFilePath = Path.Combine(_testDirectory, "mixed.jsonl");

    // 有効なJSONと無効なJSONが混在するファイルを作成
    var entries = new List<string>
        {
            JsonSerializer.Serialize(new LogEntry { Id = "1", DeviceId = "device1", Timestamp = DateTime.UtcNow, Level = "info", Message = "Valid entry" }),
            "{ This is not a valid JSON }",
            JsonSerializer.Serialize(new LogEntry { Id = "2", DeviceId = "device2", Timestamp = DateTime.UtcNow, Level = "error", Message = "Another valid entry" })
        };

    await File.WriteAllLinesAsync(testFilePath, entries);

    // Act
    var result = await service.ProcessFileAsync(testFilePath);

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeTrue(); // 処理自体は成功
    result.ProcessedRecords.Should().Be(2); // 有効なエントリのみカウント

    _validatorMock.Verify(
        v => v.ValidateAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()),
        Times.Exactly(2)); // 有効なエントリのみバリデーション
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task ProcessFileAsync_WithValidationErrors_FiltersInvalidEntries()
  {
    // Arrange
    var service = new FileProcessorService(
        _loggerMock.Object,
        _optionsMock.Object,
        _validatorMock.Object,
        _jsonProcessorMock.Object,
        _encodingDetectorMock.Object);
    var testFilePath = Path.Combine(_testDirectory, "validation_errors.jsonl");

    // バリデーションエラーを設定
    _validatorMock.Reset();
    _validatorMock
        .Setup(v => v.ValidateAsync(It.Is<LogEntry>(e => e.Id == "1"), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ValidationResult()); // 有効

    _validatorMock
        .Setup(v => v.ValidateAsync(It.Is<LogEntry>(e => e.Id == "2"), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("DeviceId", "DeviceId is required") })); // 無効

    _validatorMock
        .Setup(v => v.ValidateAsync(It.Is<LogEntry>(e => e.Id == "3"), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ValidationResult()); // 有効

    // テストファイルを作成
    var entries = new List<string>
        {
            JsonSerializer.Serialize(new LogEntry { Id = "1", DeviceId = "device1", Timestamp = DateTime.UtcNow, Level = "info", Message = "Valid entry" }),
            JsonSerializer.Serialize(new LogEntry { Id = "2", DeviceId = "", Timestamp = DateTime.UtcNow, Level = "error", Message = "Invalid entry" }),
            JsonSerializer.Serialize(new LogEntry { Id = "3", DeviceId = "device3", Timestamp = DateTime.UtcNow, Level = "warn", Message = "Another valid entry" })
        };

    await File.WriteAllLinesAsync(testFilePath, entries);

    // Act
    var result = await service.ProcessFileAsync(testFilePath);

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeTrue();
    result.ProcessedRecords.Should().Be(2); // 有効なエントリのみカウント

    _validatorMock.Verify(
        v => v.ValidateAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()),
        Times.Exactly(3)); // すべてのエントリがバリデーションされる
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task DetectEncodingAsync_WithUtf8File_ReturnsUtf8Encoding()
  {
    // Arrange
    var service = new FileProcessorService(
        _loggerMock.Object,
        _optionsMock.Object,
        _validatorMock.Object,
        _jsonProcessorMock.Object,
        _encodingDetectorMock.Object);
    var testFilePath = Path.Combine(_testDirectory, "utf8.txt");

    // UTF-8ファイルを作成
    await File.WriteAllTextAsync(testFilePath, "UTF-8テキスト", Encoding.UTF8);

    // Act
    var result = await service.DetectEncodingAsync(testFilePath);

    // Assert
    result.Should().NotBeNull();
    result.WebName.Should().Be("utf-8");
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task DetectEncodingAsync_WithUtf8BomFile_ReturnsUtf8Encoding()
  {
    // Arrange
    var service = new FileProcessorService(
        _loggerMock.Object,
        _optionsMock.Object,
        _validatorMock.Object,
        _jsonProcessorMock.Object,
        _encodingDetectorMock.Object);
    var testFilePath = Path.Combine(_testDirectory, "utf8bom.txt");

    // UTF-8 with BOMファイルを作成
    await File.WriteAllTextAsync(testFilePath, "UTF-8 BOMテキスト", new UTF8Encoding(true));

    // Act
    var result = await service.DetectEncodingAsync(testFilePath);

    // Assert
    result.Should().NotBeNull();
    result.WebName.Should().Be("utf-8");
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void ShouldProcessFile_WithMatchingExtension_ReturnsTrue()
  {
    // Arrange
    var service = new FileProcessorService(
        _loggerMock.Object,
        _optionsMock.Object,
        _validatorMock.Object,
        _jsonProcessorMock.Object,
        _encodingDetectorMock.Object);
    var testFilePath = Path.Combine(_testDirectory, "test.jsonl");
    File.WriteAllText(testFilePath, "{}");

    // Act
    var result = service.ShouldProcessFile(testFilePath);

    // Assert
    result.Should().BeTrue();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void ShouldProcessFile_WithNonMatchingExtension_ReturnsFalse()
  {
    // Arrange
    var service = new FileProcessorService(
        _loggerMock.Object,
        _optionsMock.Object,
        _validatorMock.Object,
        _jsonProcessorMock.Object,
        _encodingDetectorMock.Object);
    var testFilePath = Path.Combine(_testDirectory, "test.txt");
    File.WriteAllText(testFilePath, "text content");

    // Act
    var result = service.ShouldProcessFile(testFilePath);

    // Assert
    result.Should().BeFalse();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void ShouldProcessFile_WithLargeFile_ReturnsFalse()
  {
    // Arrange
    _config.RetentionPolicy.LargeFileSizeThreshold = 10; // 10バイト
    var service = new FileProcessorService(
        _loggerMock.Object,
        _optionsMock.Object,
        _validatorMock.Object,
        _jsonProcessorMock.Object,
        _encodingDetectorMock.Object);
    var testFilePath = Path.Combine(_testDirectory, "large.jsonl");
    File.WriteAllText(testFilePath, "This is more than 10 bytes");

    // Act
    var result = service.ShouldProcessFile(testFilePath);

    // Assert
    result.Should().BeFalse();
  }
}
