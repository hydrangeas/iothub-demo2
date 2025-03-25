namespace MachineLog.Collector.Models;

/// <summary>
/// Collector サービスの設定を定義するクラス
/// </summary>
public class CollectorConfig
{
  /// <summary>
  /// 監視対象ディレクトリのパスのリスト
  /// </summary>
  public List<string> MonitoringPaths { get; set; } = new();

  /// <summary>
  /// 監視対象のファイルフィルター（例: *.log, *.json）
  /// </summary>
  public string FileFilter { get; set; } = "*.jsonl";

  /// <summary>
  /// ファイル変更検出後の安定化待機時間（秒）
  /// </summary>
  public int StabilizationPeriodSeconds { get; set; } = 5;

  /// <summary>
  /// 最大並行処理数
  /// </summary>
  public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

  /// <summary>
  /// ファイル保持ポリシー
  /// </summary>
  public RetentionPolicy RetentionPolicy { get; set; } = new();
}

/// <summary>
/// ファイル保持ポリシーを定義するクラス
/// </summary>
public class RetentionPolicy
{
  /// <summary>
  /// 処理済みファイルの保持期間（日数）
  /// </summary>
  public int RetentionDays { get; set; } = 7;

  /// <summary>
  /// 大きいファイルの保持期間（日数）
  /// </summary>
  public int LargeFileRetentionDays { get; set; } = 30;

  /// <summary>
  /// 大きいファイルと判断するサイズ（バイト）
  /// </summary>
  public long LargeFileSizeThreshold { get; set; } = 50 * 1024 * 1024; // 50MB

  /// <summary>
  /// アーカイブディレクトリのパス
  /// </summary>
  public string? ArchiveDirectoryPath { get; set; }

  /// <summary>
  /// 処理済みファイルを圧縮するかどうか
  /// </summary>
  public bool CompressProcessedFiles { get; set; } = true;
}
