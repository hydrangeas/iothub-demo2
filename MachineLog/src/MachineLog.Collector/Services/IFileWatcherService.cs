using System.IO;

namespace MachineLog.Collector.Services;

/// <summary>
/// ファイル監視サービスのインターフェース
/// </summary>
public interface IFileWatcherService
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