using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MachineLog.Common.Batch;

/// <summary>
/// バッチ処理を行うインターフェース
/// </summary>
/// <typeparam name="T">バッチ処理の対象となる型</typeparam>
public interface IBatchProcessor<T> where T : class
{
  /// <summary>
  /// アイテムをバッチに追加する
  /// </summary>
  /// <param name="item">追加するアイテム</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>追加が成功したかどうかを示す非同期タスク</returns>
  Task<bool> AddAsync(T item, CancellationToken cancellationToken = default);

  /// <summary>
  /// 複数のアイテムをバッチに追加する
  /// </summary>
  /// <param name="items">追加するアイテムのコレクション</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>追加が成功したかどうかを示す非同期タスク</returns>
  Task<bool> AddRangeAsync(IEnumerable<T> items, CancellationToken cancellationToken = default);

  /// <summary>
  /// 現在のバッチを強制的に処理する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>処理が成功したかどうかを示す非同期タスク</returns>
  Task<bool> FlushAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// バッチ処理を開始する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>開始が成功したかどうかを示す非同期タスク</returns>
  Task<bool> StartAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// バッチ処理を停止する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>停止が成功したかどうかを示す非同期タスク</returns>
  Task<bool> StopAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// 現在のバッチサイズを取得する
  /// </summary>
  /// <returns>現在のバッチサイズ（バイト単位）</returns>
  int GetCurrentBatchSize();

  /// <summary>
  /// 現在のバッチのエントリ数を取得する
  /// </summary>
  /// <returns>現在のバッチのエントリ数</returns>
  int GetCurrentBatchCount();

  /// <summary>
  /// バッチ処理のオプションを取得する
  /// </summary>
  /// <returns>バッチ処理のオプション</returns>
  BatchProcessorOptions GetOptions();
}