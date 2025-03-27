using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MachineLog.Common.Models;

namespace MachineLog.Common.Batch;

/// <summary>
/// バッチ処理機能の使用例を示すクラス
/// </summary>
public class BatchProcessingExample
{
  private readonly IBatchProcessor<LogEntry> _batchProcessor;

  /// <summary>
  /// バッチ処理機能の使用例を示すクラスを初期化する
  /// </summary>
  public BatchProcessingExample()
  {
    // バッチ処理のオプションを設定
    var options = new BatchProcessorOptions
    {
      MaxBatchSizeInBytes = 512 * 1024, // 512KB
      MaxBatchCount = 5000,
      BatchIntervalInMilliseconds = 15000, // 15秒
      IdleTimeoutInMilliseconds = 3000, // 3秒
      MaxConcurrency = 2
    };

    // LogEntryのバッチ処理を行うクラスを初期化
    _batchProcessor = new LogEntryBatchProcessor(
        ProcessLogEntriesBatchAsync,
        options);
  }

  /// <summary>
  /// バッチ処理を開始する
  /// </summary>
  public async Task StartAsync()
  {
    // バッチ処理を開始
    await _batchProcessor.StartAsync();
    Console.WriteLine("バッチ処理を開始しました。");
  }

  /// <summary>
  /// バッチ処理を停止する
  /// </summary>
  public async Task StopAsync()
  {
    // バッチ処理を停止
    await _batchProcessor.StopAsync();
    Console.WriteLine("バッチ処理を停止しました。");
  }

  /// <summary>
  /// ログエントリを追加する
  /// </summary>
  /// <param name="logEntry">追加するログエントリ</param>
  public async Task AddLogEntryAsync(LogEntry logEntry)
  {
    // ログエントリをバッチに追加
    await _batchProcessor.AddAsync(logEntry);
  }

  /// <summary>
  /// 複数のログエントリを追加する
  /// </summary>
  /// <param name="logEntries">追加するログエントリのコレクション</param>
  public async Task AddLogEntriesAsync(IEnumerable<LogEntry> logEntries)
  {
    // 複数のログエントリをバッチに追加
    await _batchProcessor.AddRangeAsync(logEntries);
  }

  /// <summary>
  /// 現在のバッチを強制的に処理する
  /// </summary>
  public async Task FlushAsync()
  {
    // 現在のバッチを強制的に処理
    await _batchProcessor.FlushAsync();
    Console.WriteLine("バッチを強制的に処理しました。");
  }

  /// <summary>
  /// ログエントリのバッチを処理する
  /// </summary>
  /// <param name="batch">処理するバッチ</param>
  /// <returns>処理が成功したかどうかを示す非同期タスク</returns>
  private async Task<bool> ProcessLogEntriesBatchAsync(List<LogEntry> batch)
  {
    // ここで実際のバッチ処理を実装
    // 例: データベースに保存、ファイルに書き込み、外部APIに送信など

    Console.WriteLine($"{batch.Count}件のログエントリを処理しました。");

    // 処理の成功を示す
    return await Task.FromResult(true);
  }
}
