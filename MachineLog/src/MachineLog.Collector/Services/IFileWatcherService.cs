using System.IO;
using MachineLog.Collector.Models;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイル監視サービスのインターフェース
/// </summary>
public interface IFileWatcherService : IDisposable, IAsyncDisposable
{
  /// <summary>
  /// ファイル監視を開始します
  /// </summary>
  Task StartAsync(CancellationToken cancellationToken);

  /// <summary>
  /// ファイル監視を停止します
  /// </summary>
  Task StopAsync(CancellationToken cancellationToken);

  /// <summary>
  /// 監視ディレクトリを追加します
  /// </summary>
  /// <param name="directoryPath">監視対象ディレクトリのパス</param>
  /// <returns>追加された監視設定の識別子</returns>
  string AddWatchDirectory(string directoryPath);

  /// <summary>
  /// 監視ディレクトリを追加します
  /// </summary>
  /// <param name="config">監視設定</param>
  /// <returns>追加された監視設定の識別子</returns>
  string AddWatchDirectory(DirectoryWatcherConfig config);

  /// <summary>
  /// 監視ディレクトリを削除します
  /// </summary>
  /// <param name="directoryId">監視設定の識別子</param>
  /// <returns>削除に成功したかどうか</returns>
  bool RemoveWatchDirectory(string directoryId);

  /// <summary>
  /// 監視ディレクトリを削除します
  /// </summary>
  /// <param name="directoryPath">監視対象ディレクトリのパス</param>
  /// <returns>削除に成功したかどうか</returns>
  bool RemoveWatchDirectoryByPath(string directoryPath);

  /// <summary>
  /// 現在監視中のディレクトリ設定のリストを取得します
  /// </summary>
  /// <returns>監視設定のリスト</returns>
  IReadOnlyList<DirectoryWatcherConfig> GetWatchDirectories();

  /// <summary>
  /// ファイル作成イベントが発生したときに呼び出されるイベント
  /// </summary>
  event EventHandler<FileSystemEventArgs> FileCreated;

  /// <summary>
  /// ファイル変更イベントが発生したときに呼び出されるイベント
  /// </summary>
  event EventHandler<FileSystemEventArgs> FileChanged;

  /// <summary>
  /// ファイルが安定した（完全に書き込まれた）ときに呼び出されるイベント
  /// </summary>
  event EventHandler<FileSystemEventArgs> FileStabilized;
}
