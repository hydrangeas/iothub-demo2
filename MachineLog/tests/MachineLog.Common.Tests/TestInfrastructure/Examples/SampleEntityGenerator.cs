using Bogus;

namespace MachineLog.Common.Tests.TestInfrastructure.Examples;

public class SampleEntityGenerator : TestDataGenerator<SampleEntity>
{
  protected override void ConfigureRules(Faker<SampleEntity> faker)
  {
    faker
        .RuleFor(x => x.Id, f => f.Random.Int(1, 1000))
        .RuleFor(x => x.Name, f => f.Company.CompanyName())
        .RuleFor(x => x.CreatedAt, f => f.Date.Past())
        .RuleFor(x => x.IsActive, f => f.Random.Bool());
  }

  public static SampleEntityGenerator Create()
  {
    return new SampleEntityGenerator();
  }
}