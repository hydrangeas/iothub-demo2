namespace MachineLog.Common.Batch;

/// <summary>
/// バッチ処理のオプションを表すクラス
/// </summary>
public class BatchProcessorOptions
{
  /// <summary>
  /// バッチの最大サイズ（バイト単位）
  /// デフォルトは1MB
  /// </summary>
  public int MaxBatchSizeInBytes { get; set; } = 1024 * 1024; // 1MB

  /// <summary>
  /// バッチの最大エントリ数
  /// デフォルトは10,000エントリ
  /// </summary>
  public int MaxBatchCount { get; set; } = 10000;

  /// <summary>
  /// バッチ処理の間隔（ミリ秒単位）
  /// デフォルトは30秒
  /// </summary>
  public int BatchIntervalInMilliseconds { get; set; } = 30000; // 30秒

  /// <summary>
  /// アイドル状態と判断する時間（ミリ秒単位）
  /// この時間内に新しいエントリがない場合、バッチをフラッシュする
  /// デフォルトは5秒
  /// </summary>
  public int IdleTimeoutInMilliseconds { get; set; } = 5000; // 5秒

  /// <summary>
  /// 並列処理の最大数
  /// デフォルトは1（シングルスレッド）
  /// </summary>
  public int MaxConcurrency { get; set; } = 1;

  /// <summary>
  /// バッチキューの容量
  /// デフォルトは100
  /// </summary>
  public int BatchQueueCapacity { get; set; } = 100;

  /// <summary>
  /// デフォルトのオプションを取得する
  /// </summary>
  /// <returns>デフォルトのバッチ処理オプション</returns>
  public static BatchProcessorOptions Default => new BatchProcessorOptions();
}