using BenchmarkDotNet.Running;
using MachineLog.PerformanceTests.Benchmarks;

namespace MachineLog.PerformanceTests;

public class Program
{
  public static void Main(string[] args)
  {
    // FileProcessorServiceBenchmarks を実行
    var summary = BenchmarkRunner.Run<FileProcessorServiceBenchmarks>();

    // 必要に応じて他のベンチマーククラスも追加
    // var summary2 = BenchmarkRunner.Run<AnotherBenchmarkClass>();
  }
}
