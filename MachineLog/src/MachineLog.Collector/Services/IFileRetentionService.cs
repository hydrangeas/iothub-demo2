using System.Threading.Tasks;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイル保持ポリシーを実装するサービスのインターフェース
/// </summary>
public interface IFileRetentionService
{
  /// <summary>
  /// 保持ポリシーに基づいてファイルクリーンアップを実行します
  /// </summary>
  /// <param name="directoryPath">処理対象のディレクトリパス</param>
  /// <returns>Task</returns>
  Task CleanupAsync(string directoryPath);

  /// <summary>
  /// ファイルを圧縮します
  /// </summary>
  /// <param name="filePath">圧縮対象のファイルパス</param>
  /// <returns>圧縮されたファイルのパス</returns>
  Task<string> CompressFileAsync(string filePath);

  /// <summary>
  /// ディスク容量を監視します
  /// </summary>
  /// <param name="directoryPath">監視対象のディレクトリパス</param>
  /// <returns>ディスク容量が不足している場合はtrue</returns>
  Task<bool> CheckDiskSpaceAsync(string directoryPath);

  /// <summary>
  /// ディスク容量が不足している場合の緊急クリーンアップを実行します
  /// </summary>
  /// <param name="directoryPath">クリーンアップ対象のディレクトリパス</param>
  /// <returns>Task</returns>
  Task EmergencyCleanupAsync(string directoryPath);
}
