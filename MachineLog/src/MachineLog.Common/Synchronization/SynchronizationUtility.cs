using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MachineLog.Common.Synchronization
{
    /// <summary>
    /// 同期制御のためのユーティリティクラス
    /// </summary>
    public static class SynchronizationUtility
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _namedSemaphores = new();
        private static readonly ConcurrentDictionary<string, ReaderWriterLockSlim> _namedRwLocks = new();
        private static readonly ConcurrentDictionary<string, AsyncLock> _namedAsyncLocks = new();

        /// <summary>
        /// 名前付きセマフォを取得または作成します
        /// </summary>
        /// <param name="name">セマフォの名前</param>
        /// <param name="initialCount">初期カウント</param>
        /// <param name="maxCount">最大カウント</param>
        /// <returns>セマフォ</returns>
        public static SemaphoreSlim GetOrCreateSemaphore(string name, int initialCount = 1, int maxCount = 1)
        {
            return _namedSemaphores.GetOrAdd(name, _ => new SemaphoreSlim(initialCount, maxCount));
        }

        /// <summary>
        /// 名前付きリーダーライターロックを取得または作成します
        /// </summary>
        /// <param name="name">ロックの名前</param>
        /// <returns>リーダーライターロック</returns>
        public static ReaderWriterLockSlim GetOrCreateReaderWriterLock(string name)
        {
            return _namedRwLocks.GetOrAdd(name, _ => new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion));
        }

        /// <summary>
        /// 名前付き非同期ロックを取得または作成します
        /// </summary>
        /// <param name="name">ロックの名前</param>
        /// <returns>非同期ロック</returns>
        public static AsyncLock GetOrCreateAsyncLock(string name)
        {
            return _namedAsyncLocks.GetOrAdd(name, _ => new AsyncLock());
        }

        /// <summary>
        /// 複数のタスクを並列実行し、同時実行数を制限します
        /// </summary>
        /// <typeparam name="T">入力アイテムの型</typeparam>
        /// <typeparam name="TResult">結果の型</typeparam>
        /// <param name="items">処理するアイテムのコレクション</param>
        /// <param name="func">各アイテムに対して実行する関数</param>
        /// <param name="maxDegreeOfParallelism">最大並列度</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>結果のコレクション</returns>
        public static async Task<IEnumerable<TResult>> ParallelForEachAsync<T, TResult>(
            IEnumerable<T> items,
            Func<T, CancellationToken, Task<TResult>> func,
            int maxDegreeOfParallelism = 4,
            CancellationToken cancellationToken = default)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (func == null) throw new ArgumentNullException(nameof(func));
            if (maxDegreeOfParallelism <= 0) throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));

            // 同時実行数を制限するセマフォを作成
            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);
            var tasks = new List<Task<TResult>>();
            var results = new ConcurrentBag<TResult>();

            foreach (var item in items)
            {
                // キャンセルされた場合は処理を中断
                cancellationToken.ThrowIfCancellationRequested();

                // セマフォを待機（同時実行数を制限）
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                // 各アイテムを非同期で処理
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var result = await func(item, cancellationToken).ConfigureAwait(false);
                        results.Add(result);
                        return result;
                    }
                    finally
                    {
                        // 処理が完了したらセマフォを解放
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            // すべてのタスクが完了するのを待機
            await Task.WhenAll(tasks).ConfigureAwait(false);
            return results;
        }

        /// <summary>
        /// 複数のタスクを並列実行し、同時実行数を制限します（結果を返さないバージョン）
        /// </summary>
        /// <typeparam name="T">入力アイテムの型</typeparam>
        /// <param name="items">処理するアイテムのコレクション</param>
        /// <param name="func">各アイテムに対して実行する関数</param>
        /// <param name="maxDegreeOfParallelism">最大並列度</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        public static async Task ParallelForEachAsync<T>(
            IEnumerable<T> items,
            Func<T, CancellationToken, Task> func,
            int maxDegreeOfParallelism = 4,
            CancellationToken cancellationToken = default)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (func == null) throw new ArgumentNullException(nameof(func));
            if (maxDegreeOfParallelism <= 0) throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));

            // 同時実行数を制限するセマフォを作成
            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                // キャンセルされた場合は処理を中断
                cancellationToken.ThrowIfCancellationRequested();

                // セマフォを待機（同時実行数を制限）
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                // 各アイテムを非同期で処理
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await func(item, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        // 処理が完了したらセマフォを解放
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            // すべてのタスクが完了するのを待機
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        public static void DisposeAll()
        {
            foreach (var semaphore in _namedSemaphores.Values)
            {
                semaphore.Dispose();
            }
            _namedSemaphores.Clear();

            foreach (var rwLock in _namedRwLocks.Values)
            {
                rwLock.Dispose();
            }
            _namedRwLocks.Clear();

            foreach (var asyncLock in _namedAsyncLocks.Values)
            {
                asyncLock.Dispose();
            }
            _namedAsyncLocks.Clear();
        }
    }
}