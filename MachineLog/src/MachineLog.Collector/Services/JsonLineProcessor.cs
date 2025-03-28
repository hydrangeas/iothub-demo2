using FluentValidation;
using MachineLog.Common.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net; // WebUtility.HtmlEncode を使用するために追加

namespace MachineLog.Collector.Services;

/// <summary>
/// JSON Linesファイルを処理するためのクラス
/// </summary>
public class JsonLineProcessor
{
  private readonly ILogger<JsonLineProcessor> _logger;
  private readonly IValidator<LogEntry> _validator;
  private readonly JsonSerializerOptions _jsonOptions;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="validator">LogEntryバリデータ</param>
  public JsonLineProcessor(
      ILogger<JsonLineProcessor> logger,
      IValidator<LogEntry> validator)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    _jsonOptions = new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true,
      AllowTrailingCommas = true,
      ReadCommentHandling = JsonCommentHandling.Skip,
      // シリアル化/逆シリアル化の許容度を高める設定
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
  }

  /// <summary>
  /// ファイルを処理して有効なLogEntryのコレクションを返します
  /// </summary>
  /// <param name="filePath">処理対象のファイルパス</param>
  /// <param name="encoding">ファイルのエンコーディング</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>ファイル処理結果</returns>
  public async Task<JsonProcessingResult> ProcessFileAsync(
      string filePath,
      Encoding encoding,
      CancellationToken cancellationToken = default)
  {
    var invalidEntries = new List<ProcessingError>();
    var validEntries = new List<LogEntry>();

    try
    {
      // 非同期ストリームを使用して処理
      await foreach (var result in ProcessLinesAsync(filePath, encoding, cancellationToken).ConfigureAwait(false))
      {
        if (result.IsValid && result.Entry != null)
        {
          validEntries.Add(result.Entry);
        }
        else if (!result.IsValid && result.Error != null)
        {
          invalidEntries.Add(result.Error);
        }
      }

      return new JsonProcessingResult
      {
        Entries = validEntries,
        InvalidEntries = invalidEntries,
        Success = true,
        ProcessedRecords = validEntries.Count,
        InvalidRecords = invalidEntries.Count,
        FilePath = filePath
      };
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイル処理中に例外が発生しました: {FilePath}", filePath);
      throw;
    }
  }

  /// <summary>
  /// ファイルの各行を非同期ストリームとして処理します
  /// </summary>
  /// <param name="filePath">処理対象のファイルパス</param>
  /// <param name="encoding">ファイルのエンコーディング</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>処理結果の非同期ストリーム</returns>
  public async IAsyncEnumerable<LineProcessingResult> ProcessLinesAsync(
      string filePath,
      Encoding encoding,
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    // 大容量ファイル対応のためにバッファサイズとストリーム処理を最適化
    using var fileStream = new FileStream(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite,
        bufferSize: 4096,
        useAsync: true);

    using var streamReader = new StreamReader(fileStream, encoding);

    string? line;
    int lineNumber = 0;

    while ((line = await streamReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
    {
      if (cancellationToken.IsCancellationRequested)
        yield break;

      lineNumber++;

      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      LineProcessingResult result;
      try
      {
        var entry = await ProcessLineAsync(line, filePath, lineNumber, cancellationToken).ConfigureAwait(false);
        result = new LineProcessingResult
        {
          IsValid = entry != null,
          Entry = entry,
          LineNumber = lineNumber
        };
      }
      catch (JsonException ex)
      {
        _logger.LogWarning(ex, "JSON解析エラー（行 {LineNumber}）: {Line}", lineNumber, TruncateForLogging(line));
        result = new LineProcessingResult
        {
          IsValid = false,
          Error = new ProcessingError
          {
            LineNumber = lineNumber,
            Content = line,
            ErrorType = ErrorType.JsonParseError,
            ErrorMessage = ex.Message
          },
          LineNumber = lineNumber
        };
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "行の処理中にエラーが発生しました（行 {LineNumber}）", lineNumber);
        result = new LineProcessingResult
        {
          IsValid = false,
          Error = new ProcessingError
          {
            LineNumber = lineNumber,
            Content = line,
            ErrorType = ErrorType.ProcessingError,
            ErrorMessage = ex.Message
          },
          LineNumber = lineNumber
        };
      }

      yield return result;
    }
  }

  /// <summary>
  /// 1行を処理してLogEntryに変換します
  /// </summary>
  /// <param name="line">処理対象の行</param>
  /// <param name="filePath">ファイルパス（メタデータとして使用）</param>
  /// <param name="lineNumber">行番号</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>LogEntry（無効な場合はnull）</returns>
  private async Task<LogEntry?> ProcessLineAsync(
      string line,
      string filePath,
      int lineNumber,
      CancellationToken cancellationToken)
  {
    try
    {
      cancellationToken.ThrowIfCancellationRequested();

      // UTF-8エンコーディングのバイト配列からの直接デシリアライズは
      // 大量の行を処理する場合にGCプレッシャーを軽減できる
      using var jsonDocument = JsonDocument.Parse(line);
      var entry = jsonDocument.Deserialize<LogEntry>(_jsonOptions);

      if (entry == null)
      {
        _logger.LogWarning("デシリアライズ結果がnullです（行 {LineNumber}）: {Line}", lineNumber, TruncateForLogging(line));
        return null;
      }

      // ISO 8601形式の日付が正しく解析されなかった場合の修正処理
      if (entry.Timestamp == default)
      {
        // 文字列からの日付解析を試みる
        if (jsonDocument.RootElement.TryGetProperty("timestamp", out var timestampElement) &&
            timestampElement.ValueKind == JsonValueKind.String)
        {
          var timestampStr = timestampElement.GetString();
          if (!string.IsNullOrEmpty(timestampStr) &&
              DateTime.TryParse(timestampStr, out var parsedDate))
          {
            entry.Timestamp = parsedDate;
          }
        }
      }

      // メタデータを設定
      entry.SourceFile = filePath; // SourceFile はエンコードしない (パス情報のため)
      entry.ProcessedAt = DateTime.UtcNow;

      // ISO 8601形式の日付が正しく解析されなかった場合の修正処理
      if (entry.Timestamp == default)
      {
        // 文字列からの日付解析を試みる
        if (jsonDocument.RootElement.TryGetProperty("timestamp", out var timestampElement) &&
            timestampElement.ValueKind == JsonValueKind.String)
        {
          var timestampStr = timestampElement.GetString();
          if (!string.IsNullOrEmpty(timestampStr) &&
              DateTime.TryParse(timestampStr, out var parsedDate))
          {
            entry.Timestamp = parsedDate;
          }
        }
      }

      // バリデーション
      var validationResult = await _validator.ValidateAsync(entry, cancellationToken).ConfigureAwait(false);
      if (!validationResult.IsValid)
      {
        var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
        _logger.LogWarning("バリデーションエラー（行 {LineNumber}）: {Errors}", lineNumber, errors);
        return null;
      }

      // バリデーション成功後、主要な文字列プロパティをHTMLエンコードする (XSS対策の基本層)
      entry.Id = WebUtility.HtmlEncode(entry.Id);
      entry.DeviceId = WebUtility.HtmlEncode(entry.DeviceId);
      entry.Message = WebUtility.HtmlEncode(entry.Message);
      if (entry.Category != null)
      {
        entry.Category = WebUtility.HtmlEncode(entry.Category);
      }
      if (entry.Tags != null)
      {
        entry.Tags = entry.Tags.Select(tag => WebUtility.HtmlEncode(tag)).ToList();
      }
      // entry.SourceFile はファイルパスなのでエンコードしない
      if (entry.Error != null)
      {
        entry.Error.Message = WebUtility.HtmlEncode(entry.Error.Message);
        if (entry.Error.Code != null)
        {
          entry.Error.Code = WebUtility.HtmlEncode(entry.Error.Code);
        }
        // StackTrace はエンコードしない方が可読性が高い場合があるため、ここでは除外
      }

      return entry;
    }
    catch (OperationCanceledException)
    {
      // キャンセルは正常な動作として再スロー
      throw;
    }
    catch (Exception)
    {
      // その他の例外はこのメソッドの呼び出し元で処理
      throw;
    }
  }

  /// <summary>
  /// ログ出力用に文字列を切り詰めます
  /// </summary>
  private string TruncateForLogging(string input, int maxLength = 100)
  {
    if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
    {
      return input;
    }

    return input.Substring(0, maxLength) + "...";
  }
}

/// <summary>
/// 行の処理結果を表すクラス
/// </summary>
public class LineProcessingResult
{
  /// <summary>
  /// 処理が成功したかどうか
  /// </summary>
  public bool IsValid { get; set; }

  /// <summary>
  /// 処理されたエントリ（成功時）
  /// </summary>
  public LogEntry? Entry { get; set; }

  /// <summary>
  /// 処理エラー（失敗時）
  /// </summary>
  public ProcessingError? Error { get; set; }

  /// <summary>
  /// 行番号
  /// </summary>
  public int LineNumber { get; set; }
}

/// <summary>
/// 処理エラーの種類
/// </summary>
public enum ErrorType
{
  /// <summary>
  /// JSON解析エラー
  /// </summary>
  JsonParseError,

  /// <summary>
  /// バリデーションエラー
  /// </summary>
  ValidationError,

  /// <summary>
  /// 処理エラー
  /// </summary>
  ProcessingError,

  /// <summary>
  /// エンコーディングエラー
  /// </summary>
  EncodingError
}

/// <summary>
/// 処理エラーを表すクラス
/// </summary>
public class ProcessingError
{
  /// <summary>
  /// 行番号
  /// </summary>
  public int LineNumber { get; set; }

  /// <summary>
  /// エラーが発生した行の内容
  /// </summary>
  public string Content { get; set; } = string.Empty;

  /// <summary>
  /// エラーの種類
  /// </summary>
  public ErrorType ErrorType { get; set; }

  /// <summary>
  /// エラーメッセージ
  /// </summary>
  public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// JSON処理の結果を表すクラス
/// </summary>
public class JsonProcessingResult
{
  /// <summary>
  /// 処理されたエントリのコレクション
  /// </summary>
  public IReadOnlyCollection<LogEntry> Entries { get; set; } = Array.Empty<LogEntry>();

  /// <summary>
  /// 無効なエントリのコレクション
  /// </summary>
  public IReadOnlyCollection<ProcessingError> InvalidEntries { get; set; } = Array.Empty<ProcessingError>();

  /// <summary>
  /// 処理が成功したかどうか
  /// </summary>
  public bool Success { get; set; }

  /// <summary>
  /// 処理されたレコード数
  /// </summary>
  public int ProcessedRecords { get; set; }

  /// <summary>
  /// 無効なレコード数
  /// </summary>
  public int InvalidRecords { get; set; }

  /// <summary>
  /// 処理されたファイルパス
  /// </summary>
  public string FilePath { get; set; } = string.Empty;

  /// <summary>
  /// エラーメッセージ（エラーがある場合）
  /// </summary>
  public string? ErrorMessage { get; set; }
}
