namespace MachineLog.Common.Tests.TestInfrastructure.Examples;

public class SampleEntity
{
  public int Id { get; set; }
  public string Name { get; set; } = string.Empty;
  public DateTime CreatedAt { get; set; }
  public bool IsActive { get; set; }
}