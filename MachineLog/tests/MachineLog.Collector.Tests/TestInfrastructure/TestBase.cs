using AutoFixture;
using FluentAssertions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.TestInfrastructure;

/// <summary>
/// テストクラスの基底クラス
/// </summary>
public abstract class TestBase : IDisposable
{
  protected readonly IFixture Fixture;
  protected readonly ITestOutputHelper Output;

  protected TestBase(ITestOutputHelper output)
  {
    Output = output;
    Fixture = AutoFixtureFactory.Create();
  }

  /// <summary>
  /// モックの検証を行う
  /// </summary>
  protected void VerifyAllMocks(params Mock[] mocks)
  {
    foreach (var mock in mocks)
    {
      try
      {
        mock.VerifyAll();
      }
      catch (MockException ex)
      {
        Output.WriteLine($"Mock verification failed: {ex.Message}");
        throw;
      }
    }
  }

  /// <summary>
  /// テスト実行後のクリーンアップ
  /// </summary>
  public virtual void Dispose()
  {
    // 継承先でリソースの解放が必要な場合はoverrideして実装
  }
}

/// <summary>
/// 統合テスト用の基底クラス
/// </summary>
[Trait("Category", TestCategories.Integration)]
public abstract class IntegrationTestBase : TestBase
{
  protected IntegrationTestBase(ITestOutputHelper output) : base(output)
  {
  }
}

/// <summary>
/// 単体テスト用の基底クラス
/// </summary>
[Trait("Category", TestCategories.Unit)]
public abstract class UnitTestBase : TestBase
{
  protected UnitTestBase(ITestOutputHelper output) : base(output)
  {
  }
}
