using FluentValidation;
using MachineLog.Collector.Models;
using MachineLog.Common.Models;
using MachineLog.Common.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイル処理サービスの実装
/// </summary>
public class FileProcessorService : IFileProcessorService
{
  private readonly ILogger<FileProcessorService> _logger;
  private readonly CollectorConfig _config;
  private readonly IValidator<LogEntry> _validator;
  private readonly JsonLineProcessor _jsonProcessor;
  private readonly EncodingDetector _encodingDetector;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="config">設定</param>
  /// <param name="validator">LogEntryバリデータ</param>
  /// <param name="jsonProcessor">JSONプロセッサ</param>
  /// <param name="encodingDetector">エンコーディング検出器</param>
  public FileProcessorService(
      ILogger<FileProcessorService> logger,
      IOptions<CollectorConfig> config,
      IValidator<LogEntry> validator,
      JsonLineProcessor jsonProcessor,
      EncodingDetector encodingDetector)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    _jsonProcessor = jsonProcessor ?? throw new ArgumentNullException(nameof(jsonProcessor));
    _encodingDetector = encodingDetector ?? throw new ArgumentNullException(nameof(encodingDetector));
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

      // ファイルのエンコーディングを検出
      var encodingDetailResult = await GetEncodingDetectionResultAsync(filePath);
      if (!encodingDetailResult.IsValidEncoding)
      {
        _logger.LogError("ファイルの読み込みに失敗しました: {FilePath}, エラー: {Error}", filePath, encodingDetailResult.ErrorMessage);
        result.Success = false;
        result.ErrorMessage = encodingDetailResult.ErrorMessage;
        return result;
      }

      // JSONファイル処理
      var jsonResult = await _jsonProcessor.ProcessFileAsync(filePath, encodingDetailResult.Encoding, cancellationToken);

      // 処理結果を設定
      result.ProcessedRecords = jsonResult.ProcessedRecords;
      result.Success = jsonResult.Success;
      result.ErrorMessage = jsonResult.ErrorMessage;

      _logger.LogInformation(
          "ファイル処理が完了しました: {FilePath}, 有効レコード: {ValidCount}, 無効レコード: {InvalidCount}",
          filePath, jsonResult.ProcessedRecords, jsonResult.InvalidRecords);

      if (jsonResult.InvalidRecords > 0)
      {
        _logger.LogWarning(
            "ファイル内に無効なレコードがあります: {FilePath}, 無効レコード数: {InvalidCount}",
            filePath, jsonResult.InvalidRecords);

        // サンプルのエラーを出力（最大10件まで）
        var sampleErrors = jsonResult.InvalidEntries.Take(10).ToList();
        foreach (var error in sampleErrors)
        {
          _logger.LogWarning(
              "無効なレコード（行 {LineNumber}）: {ErrorType}, {ErrorMessage}",
              error.LineNumber, error.ErrorType, error.ErrorMessage);
        }
      }

      return result;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイル処理中に例外が発生しました: {FilePath}", filePath);
      result.Success = false;
      result.ErrorMessage = ex.Message;
      result.Exception = ex;
      return result;
    }
    finally
    {
      stopwatch.Stop();
      result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
    }
  }

  /// <summary>
  /// ファイルのエンコーディングを検出します（インターフェース実装）
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  /// <returns>検出されたエンコーディング</returns>
  public async Task<Encoding> DetectEncodingAsync(string filePath)
  {
    var result = await _encodingDetector.DetectEncodingAsync(filePath);
    return result.Encoding;
  }

  /// <summary>
  /// 詳細なエンコーディング検出結果を取得します
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  /// <returns>詳細なエンコーディング検出結果</returns>
  private async Task<EncodingDetector.EncodingDetectionResult> GetEncodingDetectionResultAsync(string filePath)
  {
    return await _encodingDetector.DetectEncodingAsync(filePath);
  }

  /// <summary>
  /// ファイルをフィルタリングします（処理対象かどうかを判断）
  /// </summary>
  public bool ShouldProcessFile(string filePath)
  {
    try
    {
      // ファイルが存在するか確認
      if (!File.Exists(filePath))
      {
        _logger.LogWarning("ファイルが存在しません: {FilePath}", filePath);
        return false;
      }

      // ファイル拡張子のフィルタリング
      var extension = Path.GetExtension(filePath).ToLowerInvariant();

      // フィルタリング条件の優先順位:
      // 1. 拡張子リストが設定されている場合はそれを使用
      // 2. 拡張子リストが空でワイルドカードが設定されている場合はそれを使用
      // 3. どちらも設定されていない場合は全ての拡張子を許可

      // 条件1: 拡張子リスト（優先）
      if (_config.FileExtensions.Any())
      {
        bool isAllowed = _config.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        if (!isAllowed)
        {
          _logger.LogInformation(
            "ファイル拡張子が許可リストに含まれていません: {FilePath}, 拡張子: {Extension}, 許可された拡張子: {AllowedExtensions}",
            filePath, extension, string.Join(", ", _config.FileExtensions));
          return false;
        }
      }
      // 条件2: ワイルドカードフィルター
      else if (!string.IsNullOrEmpty(_config.FileFilter) && _config.FileFilter.StartsWith("*."))
      {
        var allowedExtension = _config.FileFilter.Substring(1).ToLowerInvariant();
        bool isAllowed = extension.Equals(allowedExtension, StringComparison.OrdinalIgnoreCase);
        if (!isAllowed)
        {
          _logger.LogInformation(
            "ファイル拡張子がワイルドカードフィルターに一致しません: {FilePath}, 拡張子: {Extension}, フィルター: {Filter}",
            filePath, extension, _config.FileFilter);
          return false;
        }
      }
      // 条件3: すべて許可（どちらも設定されていない場合）
      // この場合、フィルタリングはスキップされるため追加の処理は不要

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

      // ファイルがロックされていないか確認
      try
      {
        using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
        // ファイルが開ける場合は処理可能
      }
      catch (IOException ex)
      {
        _logger.LogWarning(ex, "ファイルにアクセスできません（ロックされている可能性があります）: {FilePath}", filePath);
        return false;
      }

      return true;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイルフィルタリング中にエラーが発生しました: {FilePath}", filePath);
      return false;
    }
  }
}
