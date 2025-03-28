using BenchmarkDotNet.Attributes;
using Bogus;
using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using MachineLog.Common.Models;
using FluentValidation;
using MachineLog.Collector.Utilities;
using MachineLog.Common.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using System.Text.Json;

namespace MachineLog.PerformanceTests.Benchmarks;

[MemoryDiagnoser] // メモリ使用量を測定
public class FileProcessorServiceBenchmarks
{
  private const int LogEntryCount = 10000; // Issue #20 の要件に合わせたログエントリ数
  private string _tempFilePath = null!;
  private FileProcessorService _fileProcessorService = null!;
  private Mock<IBatchProcessorService> _batchProcessorServiceMock = null!;
  // IIoTHubService のモックは FileProcessorService のコンストラクタに不要なため削除
  // private Mock<IIoTHubService> _iotHubServiceMock = null!;
  private IValidator<LogEntry> _validator = null!;
  private JsonLineProcessor _jsonLineProcessor = null!;
  private EncodingDetector _encodingDetector = null!;


  [GlobalSetup]
  public void GlobalSetup()
  {
    // テスト用の一時ファイルを作成
    _tempFilePath = Path.Combine(Path.GetTempPath(), $"perf_test_{Guid.NewGuid()}.log");

    // Bogus を使用して大量のログエントリを生成
    var faker = new Faker<LogEntry>()
        .RuleFor(o => o.Timestamp, f => f.Date.Past(1)) // PastOffset から Past に変更
        .RuleFor(o => o.DeviceId, f => f.Random.Guid().ToString()) // MachineId から DeviceId に変更
                                                                   // Status と Value は LogEntry に存在しないため削除
                                                                   // .RuleFor(o => o.Status, f => f.PickRandom("Running", "Stopped", "Error"))
                                                                   // .RuleFor(o => o.Value, f => f.Random.Double(0, 100))
        .RuleFor(o => o.Level, f => f.PickRandom("Information", "Warning", "Error")) // Level を追加
        .RuleFor(o => o.Message, f => f.Lorem.Sentence()); // Message を追加

    var logEntries = faker.Generate(LogEntryCount);

    // ファイルに書き込み (JSON Lines形式)
    using var writer = new StreamWriter(_tempFilePath, false, Encoding.UTF8);
    foreach (var entry in logEntries)
    {
      writer.WriteLine(JsonSerializer.Serialize(entry));
    }

    // サービスのセットアップ
    _batchProcessorServiceMock = new Mock<IBatchProcessorService>();
    // _iotHubServiceMock = new Mock<IIoTHubService>(); // 不要

    // 依存関係のインスタンス化
    _validator = new LogEntryValidator();
    _encodingDetector = new EncodingDetector(NullLogger<EncodingDetector>.Instance);

    // CollectorConfig のオプションを作成 (デフォルト値を使用)
    var collectorOptions = Options.Create(new CollectorConfig());

    _jsonLineProcessor = new JsonLineProcessor(
        NullLogger<JsonLineProcessor>.Instance,
        _validator); // IOptions<CollectorConfig> は不要

    _fileProcessorService = new FileProcessorService(
        NullLogger<FileProcessorService>.Instance,
        collectorOptions, // FileProcessorService は IOptions<CollectorConfig> を必要とする
        _validator,
        _jsonLineProcessor,
        _encodingDetector); // IBatchProcessorService は不要
  }

  [GlobalCleanup]
  public void GlobalCleanup()
  {
    // 一時ファイルを削除
    if (File.Exists(_tempFilePath))
    {
      File.Delete(_tempFilePath);
    }
  }

  [Benchmark(Description = "Process a file with 10,000 log entries")] // 説明を追加
  public async Task ProcessLogFileAsync()
  {
    // ProcessFileAsync は IBatchProcessorService を引数に取らないため修正
    // await _fileProcessorService.ProcessFileAsync(_tempFilePath, CancellationToken.None);
    // FileProcessorService のインスタンスメソッドを直接呼び出すのではなく、
    // BenchmarkDotNet がインスタンスを管理し、Benchmark メソッドを実行する。
    // FileProcessorService の実際の処理を模倣するために、内部ロジックの一部を呼び出すか、
    // または、より高レベルな操作（例：ファイルを監視対象ディレクトリに配置して処理をトリガー）を
    // ベンチマークする必要があるかもしれない。
    // ここでは、簡略化のため、ProcessFileAsync を直接呼び出す形を維持するが、
    // 実際の FileProcessorService のコンストラクタ引数が変更されているため、
    // 呼び出し方を再検討する必要がある。
    // FileProcessorService の ProcessFileAsync は private または internal の可能性があるため、
    // リフレクションを使うか、テスト用に public に変更する必要があるかもしれない。
    // 現状の FileProcessorService の実装を確認する必要がある。

    // 仮実装: FileProcessorService の実装を確認後、適切な呼び出しに修正する。
    // 現時点ではコンパイルを通すためにコメントアウトまたはダミー処理とする。
    // await Task.CompletedTask;

    // FileProcessorService.ProcessFileAsync を呼び出すように修正
    // ただし、このメソッドは public である必要がある
    await _fileProcessorService.ProcessFileAsync(_tempFilePath, CancellationToken.None);
  }
}
