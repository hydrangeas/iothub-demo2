namespace MachineLog.Collector.Models;

/// <summary>
/// バッチ処理の設定を定義するクラス
/// </summary>
public class BatchConfig
{
  /// <summary>
  /// 最大バッチサイズ（バイト）
  /// </summary>
  public int MaxBatchSizeBytes { get; set; } = 1024 * 1024; // 1MB

  /// <summary>
  /// 1バッチあたりの最大アイテム数
  /// </summary>
  public int MaxBatchItems { get; set; } = 10000;

  /// <summary>
  /// バッチ処理の間隔（秒）
  /// </summary>
  public int ProcessingIntervalSeconds { get; set; } = 30;

  /// <summary>
  /// リトライポリシー
  /// </summary>
  public RetryPolicy RetryPolicy { get; set; } = new();
}

/// <summary>
/// リトライポリシーを定義するクラス
/// </summary>
public class RetryPolicy
{
  /// <summary>
  /// 最大リトライ回数
  /// </summary>
  public int MaxRetries { get; set; } = 5;

  /// <summary>
  /// 初期リトライ待機時間（秒）
  /// </summary>
  public int InitialRetryIntervalSeconds { get; set; } = 1;

  /// <summary>
  /// 最大リトライ待機時間（秒）
  /// </summary>
  public int MaxRetryIntervalSeconds { get; set; } = 30;

  /// <summary>
  /// リトライ間隔の乗数（指数バックオフ）
  /// </summary>
  public double RetryBackoffMultiplier { get; set; } = 2.0;
}
