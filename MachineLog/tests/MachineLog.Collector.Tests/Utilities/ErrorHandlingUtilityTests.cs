using FluentAssertions;
using MachineLog.Collector.Tests.TestInfrastructure;
using MachineLog.Collector.Utilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.Utilities;

public class ErrorHandlingUtilityTests : UnitTestBase
{
    private readonly Mock<ILogger<ErrorHandlingUtilityTests>> _loggerMock;

    public ErrorHandlingUtilityTests(ITestOutputHelper output) : base(output)
    {
        _loggerMock = new Mock<ILogger<ErrorHandlingUtilityTests>>();
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SafeExecute_WhenSuccessful_ReturnsTrue()
    {
        // Arrange
        bool actionExecuted = false;
        Action action = () => actionExecuted = true;

        // Act
        var result = ErrorHandlingUtility.SafeExecute(_loggerMock.Object, "TestOperation", action);

        // Assert
        result.Should().BeTrue();
        actionExecuted.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SafeExecute_WhenExceptionThrown_ReturnsFalseAndLogsError()
    {
        // Arrange
        Action action = () => throw new InvalidOperationException("Test exception");

        // Act
        var result = ErrorHandlingUtility.SafeExecute(_loggerMock.Object, "TestOperation", action);

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TestOperation")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SafeExecuteWithResult_WhenSuccessful_ReturnsExpectedResult()
    {
        // Arrange
        Func<int> func = () => 42;

        // Act
        var result = ErrorHandlingUtility.SafeExecute(_loggerMock.Object, "TestOperation", func, -1);

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SafeExecuteWithResult_WhenExceptionThrown_ReturnsDefaultValue()
    {
        // Arrange
        Func<int> func = () => throw new InvalidOperationException("Test exception");

        // Act
        var result = ErrorHandlingUtility.SafeExecute(_loggerMock.Object, "TestOperation", func, -1);

        // Assert
        result.Should().Be(-1);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TestOperation")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task SafeExecuteAsync_WhenSuccessful_ReturnsTrue()
    {
        // Arrange
        bool actionExecuted = false;
        Func<Task> func = async () => 
        {
            await Task.Delay(1);
            actionExecuted = true;
        };

        // Act
        var result = await ErrorHandlingUtility.SafeExecuteAsync(_loggerMock.Object, "TestOperation", func);

        // Assert
        result.Should().BeTrue();
        actionExecuted.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task SafeExecuteAsync_WhenExceptionThrown_ReturnsFalseAndLogsError()
    {
        // Arrange
        Func<Task> func = () => Task.FromException(new InvalidOperationException("Test exception"));

        // Act
        var result = await ErrorHandlingUtility.SafeExecuteAsync(_loggerMock.Object, "TestOperation", func);

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TestOperation")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task SafeExecuteAsyncWithResult_WhenSuccessful_ReturnsExpectedResult()
    {
        // Arrange
        Func<Task<int>> func = () => Task.FromResult(42);

        // Act
        var result = await ErrorHandlingUtility.SafeExecuteAsync(_loggerMock.Object, "TestOperation", func, -1);

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task SafeExecuteAsyncWithResult_WhenExceptionThrown_ReturnsDefaultValue()
    {
        // Arrange
        Func<Task<int>> func = () => Task.FromException<int>(new InvalidOperationException("Test exception"));

        // Act
        var result = await ErrorHandlingUtility.SafeExecuteAsync(_loggerMock.Object, "TestOperation", func, -1);

        // Assert
        result.Should().Be(-1);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TestOperation")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task SafeExecuteAsync_WhenCancelled_LogsInformationAndReturnsFalse()
    {
        // Arrange
        Func<Task> func = () => Task.FromCanceled(new CancellationToken(true));

        // Act
        var result = await ErrorHandlingUtility.SafeExecuteAsync(_loggerMock.Object, "TestOperation", func);

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TestOperation")),
                It.IsAny<OperationCanceledException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}