using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using FluentAssertions;
using MachineLog.Collector.Models;
using MachineLog.Collector.Services;
using MachineLog.Collector.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace MachineLog.Collector.Tests.Services;

[Trait("Category", TestCategories.Unit)]
public class FileRetentionServiceTests : UnitTestBase
{
  private readonly Mock<ILogger<FileRetentionService>> _loggerMock;
  private readonly Mock<IOptions<CollectorConfig>> _optionsMock;
  private readonly CollectorConfig _config;
  private readonly FileRetentionService _service;
  private readonly string _testDirectory;
  private readonly string _processedFileExtension = ".processed";
  private readonly string _archiveDirectory = "archive";

  public FileRetentionServiceTests(ITestOutputHelper output) : base(output)
  {
    _loggerMock = new Mock<ILogger<FileRetentionService>>();
    _optionsMock = new Mock<IOptions<CollectorConfig>>();

    _config = new CollectorConfig
    {
      RetentionPolicy = new RetentionPolicy
      {
        RetentionDays = 7,
        LargeFileRetentionDays = 30,
        LargeFileSizeThreshold = 50 * 1024 * 1024, // 50MB
        ArchiveDirectoryPath = _archiveDirectory,
        CompressProcessedFiles = true
      }
    };

    _optionsMock.Setup(o => o.Value).Returns(_config);
    _service = new FileRetentionService(_loggerMock.Object, _optionsMock.Object);

    // テスト用ディレクトリの作成
    _testDirectory = Path.Combine(Path.GetTempPath(), $"FileRetentionTest_{Guid.NewGuid()}");
    Directory.CreateDirectory(_testDirectory);
  }

  public override void Dispose()
  {
    // テスト用ディレクトリの削除
    try
    {
      if (Directory.Exists(_testDirectory))
      {
        Directory.Delete(_testDirectory, true);
      }
    }
    catch
    {
      // 削除に失敗した場合は無視
    }

    base.Dispose();
  }

  [Fact]
  public async Task CompressFileAsync_ファイルを圧縮して元ファイルを削除すること()
  {
    // Arrange
    var testFilePath = Path.Combine(_testDirectory, $"test_file{_processedFileExtension}");
    var expectedCompressedPath = $"{testFilePath}.gz";
    var testContent = "This is a test file content for compression";

    File.WriteAllText(testFilePath, testContent);
    File.Exists(testFilePath).Should().BeTrue();

    // Act
    var result = await _service.CompressFileAsync(testFilePath);

    // Assert
    result.Should().Be(expectedCompressedPath);
    File.Exists(expectedCompressedPath).Should().BeTrue();
    File.Exists(testFilePath).Should().BeFalse();

    // 圧縮ファイルの内容を検証
    using (var fileStream = new FileStream(expectedCompressedPath, FileMode.Open))
    using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
    using (var reader = new StreamReader(gzipStream))
    {
      var decompressedContent = await reader.ReadToEndAsync();
      decompressedContent.Should().Be(testContent);
    }
  }

  [Fact]
  public async Task CleanupAsync_保持期間を超えたファイルがアーカイブまたは削除されること()
  {
    // Arrange
    var recentFile = Path.Combine(_testDirectory, $"recent_file{_processedFileExtension}");
    var oldFile = Path.Combine(_testDirectory, $"old_file{_processedFileExtension}");
    var archiveDir = Path.Combine(_testDirectory, _archiveDirectory);

    // 最近のファイル作成
    File.WriteAllText(recentFile, "Recent file content");
    File.SetLastWriteTime(recentFile, DateTime.Now.AddDays(-1));

    // 古いファイル作成
    File.WriteAllText(oldFile, "Old file content");
    File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-10)); // 保持期間(7日)より古い

    // Act
    await _service.CleanupAsync(_testDirectory);

    // Assert
    File.Exists(recentFile).Should().BeTrue(); // 最近のファイルは残っているはず
    File.Exists(oldFile).Should().BeFalse(); // 古いファイルは移動または削除されているはず

    if (_config.RetentionPolicy.ArchiveDirectoryPath != null)
    {
      Directory.Exists(archiveDir).Should().BeTrue();
      File.Exists(Path.Combine(archiveDir, Path.GetFileName(oldFile))).Should().BeTrue();
    }
  }

  [Fact]
  public async Task CheckDiskSpaceAsync_ディスク容量が十分な場合はFalseを返すこと()
  {
    // Arrange & Act
    var result = await _service.CheckDiskSpaceAsync(_testDirectory);

    // Assert
    // テスト環境ではディスク容量が十分あるはず
    result.Should().BeFalse();
  }

  [Fact]
  public async Task EmergencyCleanupAsync_古いファイルから順に処理すること()
  {
    // Arrange
    var olderFile = Path.Combine(_testDirectory, $"older_file{_processedFileExtension}");
    var oldFile = Path.Combine(_testDirectory, $"old_file{_processedFileExtension}");
    var recentFile = Path.Combine(_testDirectory, $"recent_file{_processedFileExtension}");

    // ファイル作成（年代順）
    File.WriteAllText(olderFile, "Older file content");
    File.SetLastWriteTime(olderFile, DateTime.Now.AddDays(-20));

    File.WriteAllText(oldFile, "Old file content");
    File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-10));

    File.WriteAllText(recentFile, "Recent file content");
    File.SetLastWriteTime(recentFile, DateTime.Now.AddDays(-1));

    // Act
    await _service.EmergencyCleanupAsync(_testDirectory);

    // Assert - まず古いものから圧縮/削除されるはず
    File.Exists(olderFile).Should().BeFalse();

    // 注: 実際の動作ではディスク容量に基づいて削除するファイル数が決まりますが、
    // テスト環境では空き容量が十分あるため、必ずしも全ファイルが削除されるわけではありません
  }
}
