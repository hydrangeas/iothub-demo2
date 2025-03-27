using Microsoft.Extensions.Logging;
using System.Text;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイルのエンコーディングを検出するクラス
/// </summary>
public class EncodingDetector
{
  private readonly ILogger<EncodingDetector> _logger;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  public EncodingDetector(ILogger<EncodingDetector> logger)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
  }

  /// <summary>
  /// ファイルのエンコーディングを検出します
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>検出されたエンコーディング</returns>
  public async Task<EncodingDetectionResult> DetectEncodingAsync(string filePath, CancellationToken cancellationToken = default)
  {
    try
    {
      if (!File.Exists(filePath))
      {
        return new EncodingDetectionResult
        {
          Encoding = Encoding.UTF8,
          HasBom = false,
          IsValidEncoding = false,
          ErrorMessage = $"ファイルが存在しません: {filePath}"
        };
      }

      // BOMを確認
      var result = await CheckBomAsync(filePath, cancellationToken);
      if (result.HasBom)
      {
        return result;
      }

      // BOMがない場合は、コンテンツ解析によるエンコーディング推定
      return await AnalyzeContentEncodingAsync(filePath, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "エンコーディング検出中にエラーが発生しました: {FilePath}", filePath);
      return new EncodingDetectionResult
      {
        Encoding = Encoding.UTF8, // デフォルトはUTF-8
        HasBom = false,
        IsValidEncoding = false,
        ErrorMessage = ex.Message
      };
    }
  }

  /// <summary>
  /// ファイルのBOMを確認します
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>BOM検出結果</returns>
  private async Task<EncodingDetectionResult> CheckBomAsync(string filePath, CancellationToken cancellationToken = default)
  {
    using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    // BOMを確認
    var bom = new byte[4];
    var read = await fileStream.ReadAsync(bom, 0, 4, cancellationToken);

    // UTF-8 BOM (EF BB BF)
    if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
    {
      return new EncodingDetectionResult
      {
        Encoding = Encoding.UTF8,
        HasBom = true,
        IsValidEncoding = true
      };
    }

    // UTF-16 LE BOM (FF FE)
    if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
    {
      if (read >= 4 && bom[2] == 0 && bom[3] == 0)
      {
        // UTF-32 LE (FF FE 00 00)
        return new EncodingDetectionResult
        {
          Encoding = Encoding.UTF32,
          HasBom = true,
          IsValidEncoding = true
        };
      }

      return new EncodingDetectionResult
      {
        Encoding = Encoding.Unicode, // UTF-16 LE
        HasBom = true,
        IsValidEncoding = true
      };
    }

    // UTF-16 BE BOM (FE FF)
    if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
    {
      return new EncodingDetectionResult
      {
        Encoding = Encoding.BigEndianUnicode, // UTF-16 BE
        HasBom = true,
        IsValidEncoding = true
      };
    }

    // UTF-32 BE BOM (00 00 FE FF)
    if (read >= 4 && bom[0] == 0 && bom[1] == 0 && bom[2] == 0xFE && bom[3] == 0xFF)
    {
      return new EncodingDetectionResult
      {
        Encoding = new UTF32Encoding(true, true), // UTF-32 BE
        HasBom = true,
        IsValidEncoding = true
      };
    }

    // BOMなし
    return new EncodingDetectionResult
    {
      Encoding = Encoding.UTF8, // デフォルト（この段階では確定していない）
      HasBom = false,
      IsValidEncoding = true // 続いて内容で判断
    };
  }

  /// <summary>
  /// ファイルの内容からエンコーディングを推定します
  /// </summary>
  /// <param name="filePath">ファイルパス</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>エンコーディング推定結果</returns>
  private async Task<EncodingDetectionResult> AnalyzeContentEncodingAsync(string filePath, CancellationToken cancellationToken = default)
  {
    try
    {
      // サンプリングサイズ（最大で先頭4KBを読み込む）
      const int samplingSize = 4 * 1024;

      using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      var buffer = new byte[Math.Min(samplingSize, fileStream.Length)];
      await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

      // UTF-8の検証（不正なバイトシーケンスがないかチェック）
      if (IsValidUtf8(buffer))
      {
        return new EncodingDetectionResult
        {
          Encoding = new UTF8Encoding(false),
          HasBom = false,
          IsValidEncoding = true
        };
      }

      // 日本語のShift-JISを検証
      if (IsLikelyShiftJis(buffer))
      {
        return new EncodingDetectionResult
        {
          Encoding = Encoding.GetEncoding("shift_jis"),
          HasBom = false,
          IsValidEncoding = true,
          DetectionConfidence = 0.8f
        };
      }

      // その他の言語固有のエンコーディング検証を追加可能

      // JSON Linesの場合はUTF-8が推奨されるので、デフォルトはUTF-8
      _logger.LogWarning("エンコーディングを確定できませんでした。デフォルトのUTF-8を使用します: {FilePath}", filePath);
      return new EncodingDetectionResult
      {
        Encoding = new UTF8Encoding(false),
        HasBom = false,
        IsValidEncoding = true,
        DetectionConfidence = 0.5f,
        ErrorMessage = "エンコーディングを確定できませんでした。デフォルトのUTF-8を使用します。"
      };
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "エンコーディング分析中にエラーが発生しました: {FilePath}", filePath);
      return new EncodingDetectionResult
      {
        Encoding = new UTF8Encoding(false),
        HasBom = false,
        IsValidEncoding = false,
        ErrorMessage = ex.Message
      };
    }
  }

  /// <summary>
  /// バイト配列がUTF-8として有効かどうかを検証します
  /// </summary>
  /// <param name="buffer">検証するバイト配列</param>
  /// <returns>有効な場合はtrue</returns>
  private bool IsValidUtf8(byte[] buffer)
  {
    int i = 0;
    while (i < buffer.Length)
    {
      if (buffer[i] <= 0x7F) // ASCII範囲
      {
        i++;
        continue;
      }

      // 2バイト文字 (110xxxxx 10xxxxxx)
      if ((buffer[i] & 0xE0) == 0xC0)
      {
        if (i + 1 >= buffer.Length)
          return false;
        if ((buffer[i + 1] & 0xC0) != 0x80)
          return false;
        i += 2;
      }
      // 3バイト文字 (1110xxxx 10xxxxxx 10xxxxxx)
      else if ((buffer[i] & 0xF0) == 0xE0)
      {
        if (i + 2 >= buffer.Length)
          return false;
        if ((buffer[i + 1] & 0xC0) != 0x80 || (buffer[i + 2] & 0xC0) != 0x80)
          return false;
        i += 3;
      }
      // 4バイト文字 (11110xxx 10xxxxxx 10xxxxxx 10xxxxxx)
      else if ((buffer[i] & 0xF8) == 0xF0)
      {
        if (i + 3 >= buffer.Length)
          return false;
        if ((buffer[i + 1] & 0xC0) != 0x80 || (buffer[i + 2] & 0xC0) != 0x80 || (buffer[i + 3] & 0xC0) != 0x80)
          return false;
        i += 4;
      }
      else
      {
        return false; // 無効なUTF-8シーケンス
      }
    }

    return true;
  }

  /// <summary>
  /// バイト配列がShift-JISである可能性が高いかどうかを推定します
  /// </summary>
  /// <param name="buffer">検証するバイト配列</param>
  /// <returns>Shift-JISの可能性が高い場合はtrue</returns>
  private bool IsLikelyShiftJis(byte[] buffer)
  {
    // Shift-JIS検出のための定数
    const int MIN_SJIS_CHARS = 10;          // 最小Shift-JIS文字数
    const double MIN_SJIS_RATIO = 0.1;      // 最小Shift-JIS文字の割合

    int sjisCount = 0;
    int i = 0;

    while (i < buffer.Length - 1)
    {
      byte b1 = buffer[i];
      byte b2 = buffer[i + 1];

      // Shift_JISの2バイト文字の第1バイト
      bool isFirstByteValid =
        (b1 >= 0x81 && b1 <= 0x9F) ||
        (b1 >= 0xE0 && b1 <= 0xFC);

      // Shift_JISの2バイト文字の第2バイト
      bool isSecondByteValid =
        (b2 >= 0x40 && b2 <= 0x7E) ||
        (b2 >= 0x80 && b2 <= 0xFC);

      if (isFirstByteValid && isSecondByteValid)
      {
        sjisCount++;
        i += 2;
      }
      else
      {
        i++;
      }
    }

    // Shift_JISの2バイト文字が一定数以上あればShift_JISと判断
    return sjisCount > MIN_SJIS_CHARS && sjisCount * 2 > buffer.Length * MIN_SJIS_RATIO;
  }

  /// <summary>
  /// エンコーディング検出結果を表すクラス
  /// </summary>
  public class EncodingDetectionResult
  {
    /// <summary>
    /// 検出されたエンコーディング
    /// </summary>
    public Encoding Encoding { get; set; } = Encoding.UTF8;

    /// <summary>
    /// BOMがあるかどうか
    /// </summary>
    public bool HasBom { get; set; }

    /// <summary>
    /// 有効なエンコーディングかどうか
    /// </summary>
    public bool IsValidEncoding { get; set; }

    /// <summary>
    /// 検出信頼度（0.0～1.0）
    /// </summary>
    public float DetectionConfidence { get; set; } = 1.0f;

    /// <summary>
    /// エラーメッセージ（エラーがある場合）
    /// </summary>
    public string? ErrorMessage { get; set; }
  }
}
