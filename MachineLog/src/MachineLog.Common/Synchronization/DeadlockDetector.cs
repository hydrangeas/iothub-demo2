using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace MachineLog.Common.Synchronization
{
    /// <summary>
    /// デッドロック検出と防止のためのユーティリティクラス
    /// </summary>
    public static class DeadlockDetector
    {
        private static readonly ConcurrentDictionary<string, LockInfo> _activeLocks = new();
        private static readonly ConcurrentDictionary<int, HashSet<string>> _threadLocks = new();
        private static readonly ReaderWriterLockSlim _graphLock = new(LockRecursionPolicy.SupportsRecursion);
        private static bool _isEnabled = false;

        /// <summary>
        /// デッドロック検出を有効にします
        /// </summary>
        public static void Enable()
        {
            _isEnabled = true;
        }

        /// <summary>
        /// デッドロック検出を無効にします
        /// </summary>
        public static void Disable()
        {
            _isEnabled = false;
        }

        /// <summary>
        /// ロックの取得を記録します
        /// </summary>
        /// <param name="resourceId">リソースID</param>
        /// <param name="timeout">タイムアウト（ミリ秒）</param>
        /// <returns>デッドロックの可能性がある場合はtrue</returns>
        public static bool TryAcquireLock(string resourceId, int timeout = 0)
        {
            if (!_isEnabled) return false;
            if (string.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));

            var threadId = Thread.CurrentThread.ManagedThreadId;
            var stackTrace = new StackTrace(true);

            _graphLock.EnterUpgradeableReadLock();
            try
            {
                // 現在のスレッドが既に保持しているロックを取得
                if (!_threadLocks.TryGetValue(threadId, out var heldLocks))
                {
                    heldLocks = new HashSet<string>();
                    _threadLocks[threadId] = heldLocks;
                }

                // 既に同じリソースのロックを保持している場合は問題なし
                if (heldLocks.Contains(resourceId))
                {
                    return false;
                }

                // 新しいロック情報を作成
                var lockInfo = new LockInfo
                {
                    ResourceId = resourceId,
                    ThreadId = threadId,
                    AcquiredTime = DateTime.UtcNow,
                    StackTrace = stackTrace.ToString(),
                    Timeout = timeout
                };

                // デッドロックの可能性を検出
                if (_activeLocks.TryGetValue(resourceId, out var existingLock))
                {
                    // 既に他のスレッドがこのリソースをロックしている
                    if (existingLock.ThreadId != threadId)
                    {
                        // 他のスレッドが保持しているロックを取得
                        if (_threadLocks.TryGetValue(existingLock.ThreadId, out var otherThreadLocks))
                        {
                            // 現在のスレッドが保持しているロックを他のスレッドが待っているか確認
                            foreach (var heldLock in heldLocks)
                            {
                                if (otherThreadLocks.Contains(heldLock))
                                {
                                    // デッドロックの可能性を検出
                                    LogPotentialDeadlock(threadId, existingLock.ThreadId, resourceId, heldLock);
                                    return true;
                                }
                            }
                        }
                    }
                }

                // ロック情報を記録
                _graphLock.EnterWriteLock();
                try
                {
                    _activeLocks[resourceId] = lockInfo;
                    heldLocks.Add(resourceId);
                }
                finally
                {
                    _graphLock.ExitWriteLock();
                }

                return false;
            }
            finally
            {
                _graphLock.ExitUpgradeableReadLock();
            }
        }

        /// <summary>
        /// ロックの解放を記録します
        /// </summary>
        /// <param name="resourceId">リソースID</param>
        public static void ReleaseLock(string resourceId)
        {
            if (!_isEnabled) return;
            if (string.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));

            var threadId = Thread.CurrentThread.ManagedThreadId;

            _graphLock.EnterWriteLock();
            try
            {
                // ロック情報を削除
                _activeLocks.TryRemove(resourceId, out _);

                // スレッドのロック一覧から削除
                if (_threadLocks.TryGetValue(threadId, out var heldLocks))
                {
                    heldLocks.Remove(resourceId);
                    if (heldLocks.Count == 0)
                    {
                        _threadLocks.TryRemove(threadId, out _);
                    }
                }
            }
            finally
            {
                _graphLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 長時間保持されているロックを検出します
        /// </summary>
        /// <param name="thresholdMs">閾値（ミリ秒）</param>
        /// <returns>長時間保持されているロックのリスト</returns>
        public static List<LockInfo> DetectLongHeldLocks(int thresholdMs = 10000)
        {
            if (!_isEnabled) return new List<LockInfo>();

            var now = DateTime.UtcNow;
            var longHeldLocks = new List<LockInfo>();

            _graphLock.EnterReadLock();
            try
            {
                foreach (var lockInfo in _activeLocks.Values)
                {
                    var heldTime = (now - lockInfo.AcquiredTime).TotalMilliseconds;
                    if (heldTime > thresholdMs)
                    {
                        longHeldLocks.Add(lockInfo);
                    }
                }
            }
            finally
            {
                _graphLock.ExitReadLock();
            }

            return longHeldLocks;
        }

        /// <summary>
        /// 現在アクティブなロックの一覧を取得します
        /// </summary>
        /// <returns>アクティブなロックのリスト</returns>
        public static List<LockInfo> GetActiveLocks()
        {
            if (!_isEnabled) return new List<LockInfo>();

            _graphLock.EnterReadLock();
            try
            {
                return _activeLocks.Values.ToList();
            }
            finally
            {
                _graphLock.ExitReadLock();
            }
        }

        /// <summary>
        /// デッドロックの可能性をログに記録します
        /// </summary>
        private static void LogPotentialDeadlock(int thread1, int thread2, string resource1, string resource2)
        {
            var message = $"潜在的なデッドロックを検出しました: " +
                $"スレッド {thread1} は {resource1} を待機中で {resource2} を保持しています。" +
                $"スレッド {thread2} は {resource2} を待機中で {resource1} を保持しています。";

            Debug.WriteLine(message);
            Console.Error.WriteLine(message);

            if (_activeLocks.TryGetValue(resource1, out var lock1) && 
                _activeLocks.TryGetValue(resource2, out var lock2))
            {
                Debug.WriteLine($"スレッド {thread1} のスタックトレース: {lock1.StackTrace}");
                Debug.WriteLine($"スレッド {thread2} のスタックトレース: {lock2.StackTrace}");
            }
        }

        /// <summary>
        /// すべてのロック情報をクリアします
        /// </summary>
        public static void Clear()
        {
            _graphLock.EnterWriteLock();
            try
            {
                _activeLocks.Clear();
                _threadLocks.Clear();
            }
            finally
            {
                _graphLock.ExitWriteLock();
            }
        }
    }

    /// <summary>
    /// ロック情報を表すクラス
    /// </summary>
    public class LockInfo
    {
        /// <summary>
        /// リソースID
        /// </summary>
        public string ResourceId { get; set; }

        /// <summary>
        /// スレッドID
        /// </summary>
        public int ThreadId { get; set; }

        /// <summary>
        /// ロック取得時刻
        /// </summary>
        public DateTime AcquiredTime { get; set; }

        /// <summary>
        /// スタックトレース
        /// </summary>
        public string StackTrace { get; set; }

        /// <summary>
        /// タイムアウト（ミリ秒）
        /// </summary>
        public int Timeout { get; set; }
    }
}