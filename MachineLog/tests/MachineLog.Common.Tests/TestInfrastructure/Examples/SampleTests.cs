using AutoFixture;
using AutoFixture.AutoMoq;
using FluentAssertions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Common.Tests.TestInfrastructure.Examples;

public interface ISampleService
{
  Task<SampleEntity> GetEntityAsync(int id);
  Task<bool> UpdateEntityAsync(SampleEntity entity);
}

public class SampleTests : UnitTestBase
{
  private readonly Mock<ISampleService> _sampleServiceMock;
  private readonly SampleEntityGenerator _generator;

  public SampleTests(ITestOutputHelper output) : base(output)
  {
    _sampleServiceMock = new Mock<ISampleService>();
    _generator = SampleEntityGenerator.Create();
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public async Task GetEntityAsync_WhenEntityExists_ReturnsEntity()
  {
    // Arrange
    var expectedEntity = _generator.Generate();
    _sampleServiceMock
        .Setup(x => x.GetEntityAsync(expectedEntity.Id))
        .ReturnsAsync(expectedEntity);

    // Act
    var result = await _sampleServiceMock.Object.GetEntityAsync(expectedEntity.Id);

    // Assert
    result.Should().NotBeNull();
    result.Should().BeEquivalentTo(expectedEntity);
    VerifyAllMocks(_sampleServiceMock);
  }

  [Theory]
  [Trait("Category", TestCategories.Unit)]
  [InlineData(true)]
  [InlineData(false)]
  public async Task UpdateEntityAsync_WithValidEntity_ReturnsExpectedResult(bool expected)
  {
    // Arrange
    var entity = _generator.Generate();
    _sampleServiceMock
        .Setup(x => x.UpdateEntityAsync(entity))
        .ReturnsAsync(expected);

    // Act
    var result = await _sampleServiceMock.Object.UpdateEntityAsync(entity);

    // Assert
    result.Should().Be(expected);
    VerifyAllMocks(_sampleServiceMock);
  }

  [Fact]
  [Trait("Category", TestCategories.Unit)]
  public void Generate_CreatesMultipleEntities_WithValidData()
  {
    // Arrange
    const int count = 5;

    // Act
    var entities = _generator.Generate(count).ToList();

    // Assert
    entities.Should().HaveCount(count);
    entities.Should().AllSatisfy(entity =>
    {
      entity.Id.Should().BeGreaterThan(0);
      entity.Name.Should().NotBeNullOrEmpty();
      entity.CreatedAt.Should().BeBefore(DateTime.UtcNow);
    });
  }
}