using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using MachineLog.Collector.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイル保持ポリシーを実装するサービス
/// </summary>
public class FileRetentionService : IFileRetentionService
{
  private readonly ILogger<FileRetentionService> _logger;
  private readonly CollectorConfig _config;
  private readonly string _processedFileExtension = ".processed";
  private readonly double _diskSpaceWarningThreshold = 0.2; // 20%

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="options">コレクター設定</param>
  public FileRetentionService(
    ILogger<FileRetentionService> logger,
    IOptions<CollectorConfig> options)
  {
    _logger = logger;
    _config = options.Value;
  }

  /// <inheritdoc/>
  public async Task CleanupAsync(string directoryPath)
  {
    _logger.LogInformation("ファイルクリーンアップを開始します: {DirectoryPath}", directoryPath);

    try
    {
      if (!Directory.Exists(directoryPath))
      {
        _logger.LogWarning("指定されたディレクトリが存在しません: {DirectoryPath}", directoryPath);
        return;
      }

      var processedFiles = Directory
        .GetFiles(directoryPath, $"*{_processedFileExtension}", SearchOption.AllDirectories)
        .Select(f => new FileInfo(f))
        .ToList();

      _logger.LogInformation("{Count}個の処理済みファイルが見つかりました", processedFiles.Count);

      // ファイルの圧縮処理
      if (_config.RetentionPolicy.CompressProcessedFiles)
      {
        foreach (var file in processedFiles.Where(f => !f.Name.EndsWith(".gz") && f.LastWriteTime < DateTime.Now.AddHours(-1)))
        {
          try
          {
            await CompressFileAsync(file.FullName);
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "ファイル圧縮中にエラーが発生しました: {FileName}", file.Name);
          }
        }
      }

      // 保持期間を超えたファイルの削除
      var now = DateTime.Now;
      var largeFileThreshold = _config.RetentionPolicy.LargeFileSizeThreshold;
      var regularRetentionDays = _config.RetentionPolicy.RetentionDays;
      var largeFileRetentionDays = _config.RetentionPolicy.LargeFileRetentionDays;

      foreach (var file in processedFiles)
      {
        // ファイルサイズがしきい値以上の場合、大きなファイルとして扱う
        var isLargeFile = file.Length >= largeFileThreshold; // >= を使用して、しきい値以上のファイルを「大きい」と判定
        var retentionDays = isLargeFile ? largeFileRetentionDays : regularRetentionDays;
        var fileAge = (now - file.LastWriteTime).TotalDays;

        if (fileAge > retentionDays)
        {
          // アーカイブディレクトリへの移動
          if (!string.IsNullOrEmpty(_config.RetentionPolicy.ArchiveDirectoryPath))
          {
            var archivePath = Path.Combine(
              directoryPath,
              _config.RetentionPolicy.ArchiveDirectoryPath,
              file.Name);

            try
            {
              var archiveDir = Path.GetDirectoryName(archivePath);
              if (!string.IsNullOrEmpty(archiveDir) && !Directory.Exists(archiveDir))
              {
                Directory.CreateDirectory(archiveDir);
              }

              File.Move(file.FullName, archivePath);
              _logger.LogInformation(
                "ファイルをアーカイブしました: {FileName} -> {ArchivePath}",
                file.Name,
                archivePath);
            }
            catch (Exception ex)
            {
              _logger.LogError(
                ex,
                "ファイルのアーカイブに失敗しました: {FileName}",
                file.Name);

              // アーカイブに失敗した場合は削除を試みる
              try
              {
                File.Delete(file.FullName);
                _logger.LogInformation("ファイルを削除しました: {FileName}", file.Name);
              }
              catch (Exception deleteEx)
              {
                _logger.LogError(
                  deleteEx,
                  "ファイルの削除に失敗しました: {FileName}",
                  file.Name);
              }
            }
          }
          else
          {
            // アーカイブパスが指定されていない場合は直接削除
            try
            {
              File.Delete(file.FullName);
              _logger.LogInformation("ファイルを削除しました: {FileName}", file.Name);
            }
            catch (Exception ex)
            {
              _logger.LogError(
                ex,
                "ファイルの削除に失敗しました: {FileName}",
                file.Name);
            }
          }
        }
      }

      _logger.LogInformation("ファイルクリーンアップが完了しました: {DirectoryPath}", directoryPath);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイルクリーンアップ中にエラーが発生しました: {DirectoryPath}", directoryPath);
      throw;
    }
  }

  /// <inheritdoc/>
  public async Task<string> CompressFileAsync(string filePath)
  {
    if (!File.Exists(filePath))
    {
      throw new FileNotFoundException("圧縮対象のファイルが見つかりません", filePath);
    }

    var compressedFilePath = $"{filePath}.gz";
    _logger.LogInformation("ファイルの圧縮を開始します: {FilePath} -> {CompressedFilePath}", filePath, compressedFilePath);

    try
    {
      using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
      using (var targetStream = new FileStream(compressedFilePath, FileMode.Create))
      using (var gzipStream = new GZipStream(targetStream, CompressionMode.Compress))
      {
        await sourceStream.CopyToAsync(gzipStream);
      }

      // 圧縮後のファイルを検証
      using (var compressedFileStream = new FileStream(compressedFilePath, FileMode.Open))
      using (var gzipStream = new GZipStream(compressedFileStream, CompressionMode.Decompress))
      {
        try
        {
          using var memoryStream = new MemoryStream();
          await gzipStream.CopyToAsync(memoryStream);

          // 圧縮ファイルが読み取り可能であることを確認
          if (memoryStream.Length == 0)
          {
            throw new InvalidDataException("圧縮ファイルが正しく検証できませんでした");
          }
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "圧縮ファイルの検証に失敗しました: {CompressedFilePath}", compressedFilePath);
          File.Delete(compressedFilePath);
          throw;
        }
      }

      // 元のファイルを削除
      File.Delete(filePath);
      _logger.LogInformation("ファイルの圧縮が完了しました: {CompressedFilePath}", compressedFilePath);

      return compressedFilePath;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイルの圧縮中にエラーが発生しました: {FilePath}", filePath);

      // エラーが発生した場合、作成された不完全な圧縮ファイルを削除
      if (File.Exists(compressedFilePath))
      {
        File.Delete(compressedFilePath);
      }

      throw;
    }
  }

  /// <inheritdoc/>
  public Task<bool> CheckDiskSpaceAsync(string directoryPath)
  {
    try
    {
      // ドライブルートパスを取得し、nullチェック
      string? rootPath = Path.GetPathRoot(directoryPath);
      if (string.IsNullOrEmpty(rootPath))
      {
        _logger.LogWarning("有効なドライブルートパスを取得できませんでした: {DirectoryPath}", directoryPath);
        return Task.FromResult(false);
      }

      var driveInfo = new DriveInfo(rootPath);
      var availableSpacePercentage = (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize;

      _logger.LogInformation(
        "ディスク容量: {AvailableGB:F2} GB / {TotalGB:F2} GB ({AvailablePercentage:P2})",
        driveInfo.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0,
        driveInfo.TotalSize / 1024.0 / 1024.0 / 1024.0,
        availableSpacePercentage);

      if (availableSpacePercentage < _diskSpaceWarningThreshold)
      {
        _logger.LogWarning(
          "ディスク容量が不足しています: {AvailablePercentage:P2} < {WarningThreshold:P2}",
          availableSpacePercentage,
          _diskSpaceWarningThreshold);
        return Task.FromResult(true);
      }

      return Task.FromResult(false);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ディスク容量の確認中にエラーが発生しました: {DirectoryPath}", directoryPath);
      return Task.FromResult(false);
    }
  }

  /// <inheritdoc/>
  public async Task EmergencyCleanupAsync(string directoryPath)
  {
    _logger.LogWarning("緊急クリーンアップを開始します: {DirectoryPath}", directoryPath);

    try
    {
      if (!Directory.Exists(directoryPath))
      {
        _logger.LogWarning("指定されたディレクトリが存在しません: {DirectoryPath}", directoryPath);
        return;
      }

      // 通常のクリーンアップより積極的に削除
      var processedFiles = Directory
        .GetFiles(directoryPath, $"*{_processedFileExtension}", SearchOption.AllDirectories)
        .Select(f => new FileInfo(f))
        .OrderBy(f => f.LastWriteTime) // 古いファイルから削除
        .ToList();

      // まず圧縮されていないファイルを圧縮
      foreach (var file in processedFiles.Where(f => !f.Name.EndsWith(".gz")))
      {
        try
        {
          await CompressFileAsync(file.FullName);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "緊急クリーンアップ中のファイル圧縮でエラーが発生しました: {FileName}", file.Name);
        }
      }

      // ディスク容量が20%を超えるまで古いファイルから削除
      bool diskSpaceLow = true;
      int deletedCount = 0;

      // 圧縮後のファイルリストを再取得
      processedFiles = Directory
        .GetFiles(directoryPath, $"*{_processedFileExtension}*", SearchOption.AllDirectories)
        .Select(f => new FileInfo(f))
        .OrderBy(f => f.LastWriteTime)
        .ToList();

      while (diskSpaceLow && deletedCount < processedFiles.Count)
      {
        var fileToDelete = processedFiles[deletedCount];
        try
        {
          File.Delete(fileToDelete.FullName);
          _logger.LogInformation("緊急クリーンアップでファイルを削除しました: {FileName}", fileToDelete.Name);
          deletedCount++;

          // 10ファイルごとにディスク容量をチェック
          if (deletedCount % 10 == 0)
          {
            diskSpaceLow = await CheckDiskSpaceAsync(directoryPath);
          }
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "緊急クリーンアップ中のファイル削除でエラーが発生しました: {FileName}", fileToDelete.Name);
          deletedCount++;
        }
      }

      _logger.LogWarning("緊急クリーンアップが完了しました。{DeletedCount}個のファイルを削除しました。", deletedCount);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "緊急クリーンアップ中にエラーが発生しました: {DirectoryPath}", directoryPath);
      throw;
    }
  }
}
