using FluentValidation;
using MachineLog.Collector.Models;
using MachineLog.Common.Models;
using MachineLog.Common.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイル処理サービスの実装
/// </summary>
public class FileProcessorService : IFileProcessorService
{
  private readonly ILogger<FileProcessorService> _logger;
  private readonly CollectorConfig _config;
  private readonly IValidator<LogEntry> _validator;
  private static readonly JsonSerializerOptions _jsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip
  };

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="config">設定</param>
  /// <param name="validator">LogEntryバリデータ</param>
  public FileProcessorService(
      ILogger<FileProcessorService> logger,
      IOptions<CollectorConfig> config,
      IValidator<LogEntry> validator)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    _validator = validator ?? throw new ArgumentNullException(nameof(validator));
  }

  /// <summary>
  /// ファイルを処理します
  /// </summary>
  public async Task<FileProcessingResult> ProcessFileAsync(string filePath, CancellationToken cancellationToken = default)
  {
    var result = new FileProcessingResult
    {
      Success = false,
      ProcessedRecords = 0,
      FileSizeBytes = new FileInfo(filePath).Length
    };

    var stopwatch = Stopwatch.StartNew();

    try
    {
      _logger.LogInformation("ファイル処理を開始します: {FilePath}", filePath);

      if (!ShouldProcessFile(filePath))
      {
        _logger.LogInformation("ファイルは処理対象外です: {FilePath}", filePath);
        result.Success = true;
        return result;
      }

      var encoding = await DetectEncodingAsync(filePath);
      var validEntries = new List<LogEntry>();
      var invalidEntries = new List<(string Line, string Error)>();

      // JSON Lines形式のファイルを1行ずつ読み込んで処理
      using (var streamReader = new StreamReader(filePath, encoding))
      {
        string? line;
        int lineNumber = 0;

        while ((line = await streamReader.ReadLineAsync(cancellationToken)) != null)
        {
          lineNumber++;

          if (string.IsNullOrWhiteSpace(line))
          {
            continue;
          }

          try
          {
            var entry = JsonSerializer.Deserialize<LogEntry>(line, _jsonOptions);

            if (entry == null)
            {
              invalidEntries.Add((line, "デシリアライズ結果がnullです"));
              continue;
            }

            // ソースファイル情報を設定
            entry.SourceFile = filePath;
            entry.ProcessedAt = DateTime.UtcNow;

            // バリデーション
            var validationResult = await _validator.ValidateAsync(entry, cancellationToken);
            if (!validationResult.IsValid)
            {
              var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
              invalidEntries.Add((line, errors));
              continue;
            }

            validEntries.Add(entry);
          }
          catch (JsonException ex)
          {
            _logger.LogWarning(ex, "JSON解析エラー（行 {LineNumber}）: {Line}", lineNumber, line);
            invalidEntries.Add((line, $"JSON解析エラー: {ex.Message}"));
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "ファイル処理中にエラーが発生しました（行 {LineNumber}）: {Line}", lineNumber, line);
            invalidEntries.Add((line, $"処理エラー: {ex.Message}"));
          }
        }
      }

      // 処理結果を設定
      result.ProcessedRecords = validEntries.Count;
      result.Success = true;

      _logger.LogInformation(
          "ファイル処理が完了しました: {FilePath}, 有効レコード: {ValidCount}, 無効レコード: {InvalidCount}",
          filePath, validEntries.Count, invalidEntries.Count);

      if (invalidEntries.Any())
      {
        _logger.LogWarning(
            "ファイル内に無効なレコードがあります: {FilePath}, 無効レコード数: {InvalidCount}",
            filePath, invalidEntries.Count);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイル処理中に例外が発生しました: {FilePath}", filePath);
      result.Success = false;
      result.ErrorMessage = ex.Message;
      result.Exception = ex;
    }
    finally
    {
      stopwatch.Stop();
      result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
    }

    return result;
  }

  /// <summary>
  /// ファイルのエンコーディングを検出します
  /// </summary>
  public async Task<Encoding> DetectEncodingAsync(string filePath)
  {
    // 簡易的なエンコーディング検出
    // 実際のプロジェクトではより高度な検出ロジックが必要かもしれません
    try
    {
      using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

      // BOMを確認
      var bom = new byte[4];
      var read = await fileStream.ReadAsync(bom, 0, 4);
      fileStream.Position = 0;

      if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
      {
        return Encoding.BigEndianUnicode; // UTF-16 BE
      }
      if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
      {
        if (read >= 4 && bom[2] == 0 && bom[3] == 0)
        {
          return Encoding.UTF32; // UTF-32 LE
        }
        return Encoding.Unicode; // UTF-16 LE
      }
      if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
      {
        return Encoding.UTF8; // UTF-8 with BOM
      }
      if (read >= 4 && bom[0] == 0 && bom[1] == 0 && bom[2] == 0xFE && bom[3] == 0xFF)
      {
        return Encoding.GetEncoding("utf-32BE"); // UTF-32 BE
      }

      // BOMがない場合はUTF-8と仮定
      return Encoding.UTF8;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "エンコーディング検出中にエラーが発生しました: {FilePath}", filePath);
      return Encoding.UTF8; // デフォルトはUTF-8
    }
  }

  /// <summary>
  /// ファイルをフィルタリングします（処理対象かどうかを判断）
  /// </summary>
  public bool ShouldProcessFile(string filePath)
  {
    // ファイル拡張子が設定と一致するか確認
    var extension = Path.GetExtension(filePath).ToLowerInvariant();
    var fileFilter = _config.FileFilter.ToLowerInvariant();

    // ワイルドカードを処理
    if (fileFilter.StartsWith("*."))
    {
      var allowedExtension = fileFilter.Substring(1);
      return extension.Equals(allowedExtension, StringComparison.OrdinalIgnoreCase);
    }

    // ファイルサイズが大きすぎないか確認
    var fileInfo = new FileInfo(filePath);
    var maxSizeBytes = _config.RetentionPolicy.LargeFileSizeThreshold;
    if (fileInfo.Length > maxSizeBytes)
    {
      _logger.LogWarning(
          "ファイルサイズが大きすぎます: {FilePath}, サイズ: {FileSize} バイト, 最大サイズ: {MaxSize} バイト",
          filePath, fileInfo.Length, maxSizeBytes);
      return false;
    }

    return true;
  }
}