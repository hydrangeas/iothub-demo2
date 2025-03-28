using FluentAssertions;
using MachineLog.Collector.Tests.TestInfrastructure;
using MachineLog.Collector.Utilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.Utilities;

public class ResourceUtilityTests : UnitTestBase
{
    private readonly Mock<ILogger<ResourceUtilityTests>> _loggerMock;

    public ResourceUtilityTests(ITestOutputHelper output) : base(output)
    {
        _loggerMock = new Mock<ILogger<ResourceUtilityTests>>();
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SafeDispose_WhenResourceIsNull_ReturnsTrue()
    {
        // Arrange
        IDisposable? resource = null;

        // Act
        var result = ResourceUtility.SafeDispose(_loggerMock.Object, resource, "TestResource");

        // Assert
        result.Should().BeTrue();
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SafeDispose_WhenResourceDisposesSuccessfully_ReturnsTrue()
    {
        // Arrange
        var disposableMock = new Mock<IDisposable>();
        disposableMock.Setup(x => x.Dispose()).Verifiable();

        // Act
        var result = ResourceUtility.SafeDispose(_loggerMock.Object, disposableMock.Object, "TestResource");

        // Assert
        result.Should().BeTrue();
        disposableMock.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SafeDispose_WhenResourceThrowsException_ReturnsFalseAndLogsWarning()
    {
        // Arrange
        var disposableMock = new Mock<IDisposable>();
        disposableMock.Setup(x => x.Dispose()).Throws(new InvalidOperationException("Test exception"));

        // Act
        var result = ResourceUtility.SafeDispose(_loggerMock.Object, disposableMock.Object, "TestResource");

        // Assert
        result.Should().BeFalse();
        disposableMock.Verify(x => x.Dispose(), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TestResource")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task SafeDisposeAsync_WhenResourceIsNull_ReturnsTrue()
    {
        // Arrange
        IAsyncDisposable? resource = null;

        // Act
        var result = await ResourceUtility.SafeDisposeAsync(_loggerMock.Object, resource, "TestResource");

        // Assert
        result.Should().BeTrue();
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task SafeDisposeAsync_WhenResourceDisposesSuccessfully_ReturnsTrue()
    {
        // Arrange
        var disposableMock = new Mock<IAsyncDisposable>();
        disposableMock.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();

        // Act
        var result = await ResourceUtility.SafeDisposeAsync(_loggerMock.Object, disposableMock.Object, "TestResource");

        // Assert
        result.Should().BeTrue();
        disposableMock.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task SafeDisposeAsync_WhenResourceThrowsException_ReturnsFalseAndLogsWarning()
    {
        // Arrange
        var disposableMock = new Mock<IAsyncDisposable>();
        disposableMock.Setup(x => x.DisposeAsync()).Throws(new InvalidOperationException("Test exception"));

        // Act
        var result = await ResourceUtility.SafeDisposeAsync(_loggerMock.Object, disposableMock.Object, "TestResource");

        // Assert
        result.Should().BeFalse();
        disposableMock.Verify(x => x.DisposeAsync(), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TestResource")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ExecuteWithTimeoutAsync_WhenSuccessful_ReturnsExpectedResult()
    {
        // Arrange
        Func<CancellationToken, Task<int>> func = (ct) => Task.FromResult(42);

        // Act
        var result = await ResourceUtility.ExecuteWithTimeoutAsync(
            _loggerMock.Object, "TestOperation", func, 5, -1);

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ExecuteWithTimeoutAsync_WhenTimedOut_ReturnsDefaultValue()
    {
        // Arrange
        var manualResetEvent = new ManualResetEventSlim(false);

        Func<CancellationToken, Task<int>> func = async (ct) =>
        {
            try
            {
                // 完了しないタスクをシミュレート
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return 42;
            }
            finally
            {
                // キャンセルされたことを確認するためにイベントを発行
                manualResetEvent.Set();
            }
        };

        // Act
        var result = await ResourceUtility.ExecuteWithTimeoutAsync(
            _loggerMock.Object, "TestOperation", func, 1, -1);

        // Assert
        // タイムアウトが正しく機能したことを確認
        result.Should().Be(-1);

        // キャンセルが発行されたことを確認（最大5秒待機）
        manualResetEvent.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("TestOperation") && o.ToString()!.Contains("タイムアウト")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // リソースのクリーンアップ
        manualResetEvent.Dispose();
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ExecuteWithTimeoutAsync_WhenExceptionThrown_ReturnsDefaultValue()
    {
        // Arrange
        Func<CancellationToken, Task<int>> func = (ct) =>
            Task.FromException<int>(new InvalidOperationException("Test exception"));

        // Act
        var result = await ResourceUtility.ExecuteWithTimeoutAsync(
            _loggerMock.Object, "TestOperation", func, 5, -1);

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
}
