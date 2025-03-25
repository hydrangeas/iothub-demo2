using Bogus;

namespace MachineLog.Common.Tests.TestInfrastructure;

/// <summary>
/// テストデータジェネレーターの基底クラス
/// </summary>
public abstract class TestDataGenerator<T> where T : class
{
  protected readonly Faker<T> Faker;

  protected TestDataGenerator()
  {
    // 一貫性のあるデータ生成のためにシードを固定
    Randomizer.Seed = new Random(8675309);

    Faker = new Faker<T>();
    ConfigureRules(Faker);
  }

  /// <summary>
  /// Fakerのルールを設定
  /// </summary>
  protected abstract void ConfigureRules(Faker<T> faker);

  /// <summary>
  /// 単一のテストデータを生成
  /// </summary>
  public T Generate()
  {
    return Faker.Generate();
  }

  /// <summary>
  /// 指定した数のテストデータを生成
  /// </summary>
  public IEnumerable<T> Generate(int count)
  {
    return Faker.Generate(count);
  }

  /// <summary>
  /// カスタマイズされたテストデータを生成
  /// </summary>
  public T Generate(Action<Faker<T>> customization)
  {
    var customFaker = new Faker<T>();
    ConfigureRules(customFaker);
    customization(customFaker);
    return customFaker.Generate();
  }

  /// <summary>
  /// カスタマイズされたテストデータを指定した数生成
  /// </summary>
  public IEnumerable<T> Generate(int count, Action<Faker<T>> customization)
  {
    var customFaker = new Faker<T>();
    ConfigureRules(customFaker);
    customization(customFaker);
    return customFaker.Generate(count);
  }
}