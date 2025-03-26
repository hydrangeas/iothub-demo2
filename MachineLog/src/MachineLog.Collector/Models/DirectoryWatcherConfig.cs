using System.Collections.Generic;

namespace MachineLog.Collector.Models;

/// <summary>
/// ディレクトリ監視の設定を定義するクラス
/// </summary>
public class DirectoryWatcherConfig
{
  /// <summary>
  /// 監視対象ディレクトリのパス
  /// </summary>
  public string Path { get; set; } = string.Empty;

  /// <summary>
  /// 監視対象のファイルフィルター（例: *.log, *.json）
  /// </summary>
  public string FileFilter { get; set; } = "*.jsonl";

  /// <summary>
  /// サブディレクトリを含めるかどうか
  /// </summary>
  public bool IncludeSubdirectories { get; set; } = true;

  /// <summary>
  /// 監視する変更通知の種類
  /// </summary>
  public NotifyFilters NotifyFilters { get; set; } = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;

  /// <summary>
  /// 一意の識別子
  /// </summary>
  public string Id { get; } = Guid.NewGuid().ToString();

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="path">監視対象ディレクトリのパス</param>
  public DirectoryWatcherConfig(string path)
  {
    Path = path ?? throw new ArgumentNullException(nameof(path));
  }

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="path">監視対象ディレクトリのパス</param>
  /// <param name="fileFilter">監視対象のファイルフィルター</param>
  public DirectoryWatcherConfig(string path, string fileFilter) : this(path)
  {
    FileFilter = fileFilter ?? throw new ArgumentNullException(nameof(fileFilter));
  }
}