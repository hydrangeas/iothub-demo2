using Bogus;

namespace MachineLog.Common.Tests.TestInfrastructure;

/// <summary>
/// テストデータジェネレーターの基底クラス
/// </summary>
public abstract class TestDataGenerator<T> where T : class
{
  protected readonly Faker<T> Faker;

  /// <summary>
  /// テストの再現性を確保するための固定シード値
  /// </summary>
  private const int DefaultSeed = 12345;

  protected TestDataGenerator()
  {
    // テストの再現性を確保するために固定シードを使用
    // 継承先でSeedプロパティをオーバーライドすることで、
    // 特定のテストケースで異なるシードを使用することも可能
    var random = new Random(Seed);
    Randomizer.Seed = random;

    Faker = new Faker<T>();
    ConfigureRules(Faker);
  }

  /// <summary>
  /// テストデータ生成に使用するシード値
  /// デフォルトでは固定値を使用し、テストの再現性を確保
  /// </summary>
  protected virtual int Seed => DefaultSeed;

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