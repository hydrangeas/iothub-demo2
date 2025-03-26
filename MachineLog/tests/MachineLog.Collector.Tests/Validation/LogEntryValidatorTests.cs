using FluentAssertions;
using MachineLog.Collector.Tests.TestInfrastructure;
using MachineLog.Common.Models;
using MachineLog.Common.Validation;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.Validation;

public class LogEntryValidatorTests : UnitTestBase
{
  private readonly LogEntryValidator _validator;

  public LogEntryValidatorTests(ITestOutputHelper output) : base(output)
  {
    _validator = new LogEntryValidator();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithValidLogEntry_ShouldPass()
  {
    // Arrange
    var logEntry = new LogEntry
    {
      Id = "test-id-123",
      Timestamp = DateTime.UtcNow.AddMinutes(-5),
      DeviceId = "device-001",
      Level = "info",
      Message = "This is a test message",
      Category = "Test",
      Tags = new List<string> { "test", "validation" },
      Data = new Dictionary<string, object> { { "key1", "value1" } }
    };

    // Act
    var result = _validator.Validate(logEntry);

    // Assert
    result.IsValid.Should().BeTrue();
    result.Errors.Should().BeEmpty();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithMissingRequiredFields_ShouldFail()
  {
    // Arrange
    var logEntry = new LogEntry
    {
      // Missing Id, Timestamp, DeviceId, Level, Message
      Category = "Test",
      Tags = new List<string> { "test" }
    };

    // Act
    var result = _validator.Validate(logEntry);

    // Assert
    result.IsValid.Should().BeFalse();
    result.Errors.Should().HaveCount(5); // 5つの必須フィールドが欠けている

    result.Errors.Select(e => e.PropertyName).Should().Contain("Id");
    result.Errors.Select(e => e.PropertyName).Should().Contain("Timestamp");
    result.Errors.Select(e => e.PropertyName).Should().Contain("DeviceId");
    result.Errors.Select(e => e.PropertyName).Should().Contain("Level");
    result.Errors.Select(e => e.PropertyName).Should().Contain("Message");
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithInvalidTimestamp_ShouldFail()
  {
    // Arrange
    var logEntry = new LogEntry
    {
      Id = "test-id-123",
      Timestamp = DateTime.UtcNow.AddDays(2), // 未来の日時
      DeviceId = "device-001",
      Level = "info",
      Message = "This is a test message"
    };

    // Act
    var result = _validator.Validate(logEntry);

    // Assert
    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle();
    result.Errors[0].PropertyName.Should().Be("Timestamp");
  }

  [Theory]
  [InlineData("trace")]
  [InlineData("debug")]
  [InlineData("info")]
  [InlineData("information")]
  [InlineData("warn")]
  [InlineData("warning")]
  [InlineData("error")]
  [InlineData("fatal")]
  [InlineData("critical")]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithValidLogLevel_ShouldPass(string level)
  {
    // Arrange
    var logEntry = new LogEntry
    {
      Id = "test-id-123",
      Timestamp = DateTime.UtcNow.AddMinutes(-5),
      DeviceId = "device-001",
      Level = level,
      Message = "This is a test message"
    };

    // Act
    var result = _validator.Validate(logEntry);

    // Assert
    result.IsValid.Should().BeTrue();
  }

  [Theory]
  [InlineData("")]
  [InlineData("unknown")]
  [InlineData("log")]
  [InlineData("severe")]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithInvalidLogLevel_ShouldFail(string level)
  {
    // Arrange
    var logEntry = new LogEntry
    {
      Id = "test-id-123",
      Timestamp = DateTime.UtcNow.AddMinutes(-5),
      DeviceId = "device-001",
      Level = level,
      Message = "This is a test message"
    };

    // Act
    var result = _validator.Validate(logEntry);

    // Assert
    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle();
    result.Errors[0].PropertyName.Should().Be("Level");
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithTooLongId_ShouldFail()
  {
    // Arrange
    var logEntry = new LogEntry
    {
      Id = new string('a', 51), // 51文字（上限は50文字）
      Timestamp = DateTime.UtcNow.AddMinutes(-5),
      DeviceId = "device-001",
      Level = "info",
      Message = "This is a test message"
    };

    // Act
    var result = _validator.Validate(logEntry);

    // Assert
    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle();
    result.Errors[0].PropertyName.Should().Be("Id");
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithTooLongDeviceId_ShouldFail()
  {
    // Arrange
    var logEntry = new LogEntry
    {
      Id = "test-id-123",
      Timestamp = DateTime.UtcNow.AddMinutes(-5),
      DeviceId = new string('a', 101), // 101文字（上限は100文字）
      Level = "info",
      Message = "This is a test message"
    };

    // Act
    var result = _validator.Validate(logEntry);

    // Assert
    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle();
    result.Errors[0].PropertyName.Should().Be("DeviceId");
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithInvalidTags_ShouldFail()
  {
    // Arrange
    var logEntry = new LogEntry
    {
      Id = "test-id-123",
      Timestamp = DateTime.UtcNow.AddMinutes(-5),
      DeviceId = "device-001",
      Level = "info",
      Message = "This is a test message",
      Tags = new List<string> { "", new string('a', 51) } // 空のタグと51文字のタグ（上限は50文字）
    };

    // Act
    var result = _validator.Validate(logEntry);

    // Assert
    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle();
    result.Errors[0].PropertyName.Should().Be("Tags");
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithErrorInfo_ShouldValidateErrorInfo()
  {
    // Arrange
    var logEntry = new LogEntry
    {
      Id = "test-id-123",
      Timestamp = DateTime.UtcNow.AddMinutes(-5),
      DeviceId = "device-001",
      Level = "error",
      Message = "Error occurred",
      Error = new ErrorInfo
      {
        // Message is missing
        Code = "E001",
        StackTrace = "at Method() in File.cs:line 10"
      }
    };

    // Act
    var result = _validator.Validate(logEntry);

    // Assert
    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle();
    result.Errors[0].PropertyName.Should().Be("Error.Message");
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithValidErrorInfo_ShouldPass()
  {
    // Arrange
    var logEntry = new LogEntry
    {
      Id = "test-id-123",
      Timestamp = DateTime.UtcNow.AddMinutes(-5),
      DeviceId = "device-001",
      Level = "error",
      Message = "Error occurred",
      Error = new ErrorInfo
      {
        Message = "Detailed error message",
        Code = "E001",
        StackTrace = "at Method() in File.cs:line 10"
      }
    };

    // Act
    var result = _validator.Validate(logEntry);

    // Assert
    result.IsValid.Should().BeTrue();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void Validate_WithJsonDeserialization_ShouldValidateCorrectly()
  {
    // Arrange
    var json = @"{
            ""id"": ""test-id-123"",
            ""timestamp"": ""2023-01-01T12:00:00Z"",
            ""deviceId"": ""device-001"",
            ""level"": ""info"",
            ""message"": ""This is a test message"",
            ""category"": ""Test"",
            ""tags"": [""test"", ""validation""],
            ""data"": {
                ""key1"": ""value1"",
                ""key2"": 123
            }
        }";

    var logEntry = JsonSerializer.Deserialize<LogEntry>(json);

    // Act
    var result = _validator.Validate(logEntry!);

    // Assert
    result.IsValid.Should().BeTrue();
  }
}