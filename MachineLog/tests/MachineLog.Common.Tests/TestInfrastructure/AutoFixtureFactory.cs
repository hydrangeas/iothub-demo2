using AutoFixture;
using AutoFixture.AutoMoq;
using AutoFixture.Xunit2;

namespace MachineLog.Common.Tests.TestInfrastructure;

/// <summary>
/// AutoFixtureのファクトリークラス
/// </summary>
public static class AutoFixtureFactory
{
  /// <summary>
  /// 基本的なAutoFixtureインスタンスを作成
  /// </summary>
  public static IFixture Create()
  {
    var fixture = new Fixture();
    fixture.Customize(new AutoMoqCustomization());
    return fixture;
  }

  /// <summary>
  /// カスタマイズされたAutoFixtureインスタンスを作成
  /// </summary>
  public static IFixture CreateCustomized(Action<IFixture> customization)
  {
    var fixture = Create();
    customization(fixture);
    return fixture;
  }

  /// <summary>
  /// AutoDataAttributeを作成
  /// </summary>
  public static CustomAutoDataAttribute CreateAttribute()
  {
    return new CustomAutoDataAttribute(() => Create());
  }

  /// <summary>
  /// カスタマイズされたAutoDataAttributeを作成
  /// </summary>
  public static CustomAutoDataAttribute CreateCustomizedAttribute(Action<IFixture> customization)
  {
    return new CustomAutoDataAttribute(() =>
    {
      var fixture = Create();
      customization(fixture);
      return fixture;
    });
  }
}