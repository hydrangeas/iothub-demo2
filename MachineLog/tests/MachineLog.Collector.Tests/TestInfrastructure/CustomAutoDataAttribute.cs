using AutoFixture;
using AutoFixture.Xunit2;

namespace MachineLog.Collector.Tests.TestInfrastructure;

/// <summary>
/// カスタマイズ可能なAutoDataAttribute
/// </summary>
public class CustomAutoDataAttribute : AutoDataAttribute
{
  public CustomAutoDataAttribute(Func<IFixture> fixtureFactory) : base(() =>
  {
    var fixture = fixtureFactory();
    return fixture;
  })
  {
  }
}