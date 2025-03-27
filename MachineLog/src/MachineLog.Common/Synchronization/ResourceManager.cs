using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MachineLog.Common.Synchronization
{
    /// <summary>
    /// スレッドセーフなリソース管理を提供するクラス
    /// </summary>
    /// <typeparam name="TKey">リソースのキーの型</typeparam>
    /// <typeparam name="TResource">リソースの型</typeparam>
    public class ResourceManager<TKey, TResource> : IDisposable where TResource : class
    {
        private readonly ConcurrentDictionary<TKey, ResourceEntry> _resources = new();
        private readonly Func<TKey, CancellationToken, Task<TResource>> _resourceFactory;
        private readonly Action<TResource> _resourceCleanup;
        private readonly TimeSpan _resourceTimeout;
        private readonly Timer _cleanupTimer;
        private readonly SemaphoreSlim _cleanupLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;

        /// <summary>
        /// ResourceManagerを初期化します
        /// </summary>
        /// <param name="resourceFactory">リソース作成ファクトリ</param>
        /// <param name="resourceCleanup">リソース解放処理</param>
        /// <param name="resourceTimeout">リソースのタイムアウト</param>
        /// <param name="cleanupInterval">クリーンアップの間隔</param>
        public ResourceManager(
            Func<TKey, CancellationToken, Task<TResource>> resourceFactory,
            Action<TResource> resourceCleanup = null,
            TimeSpan? resourceTimeout = null,
            TimeSpan? cleanupInterval = null)
        {
            _resourceFactory = resourceFactory ?? throw new ArgumentNullException(nameof(resourceFactory));
            _resourceCleanup = resourceCleanup;
            _resourceTimeout = resourceTimeout ?? TimeSpan.FromMinutes(30);
            
            // クリーンアップタイマーを設定
            var interval = cleanupInterval ?? TimeSpan.FromMinutes(5);
            _cleanupTimer = new Timer(CleanupCallback, null, interval, interval);
        }

        /// <summary>
        /// リソースを取得または作成します
        /// </summary>
        /// <param name="key">リソースのキー</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>リソース</returns>
        public async Task<TResource> GetOrCreateAsync(TKey key, CancellationToken cancellationToken = default)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ResourceManager<TKey, TResource>));

            // 既存のリソースを取得
            if (_resources.TryGetValue(key, out var entry))
            {
                // 最終アクセス時間を更新
                entry.LastAccessed = DateTime.UtcNow;
                return entry.Resource;
            }

            // 新しいリソースを作成
            var resource = await _resourceFactory(key, cancellationToken).ConfigureAwait(false);
            
            // リソースエントリを作成
            var newEntry = new ResourceEntry
            {
                Resource = resource,
                Created = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow
            };

            // リソースを登録（既に他のスレッドが作成していた場合は、そちらを使用）
            var resultEntry = _resources.GetOrAdd(key, newEntry);
            
            // 他のスレッドが作成したリソースが使用された場合、自分が作成したリソースをクリーンアップ
            if (resultEntry != newEntry && _resourceCleanup != null)
            {
                _resourceCleanup(resource);
            }

            return resultEntry.Resource;
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="key">リソースのキー</param>
        /// <returns>解放に成功したかどうか</returns>
        public bool Release(TKey key)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ResourceManager<TKey, TResource>));

            if (_resources.TryRemove(key, out var entry) && _resourceCleanup != null)
            {
                _resourceCleanup(entry.Resource);
                return true;
            }

            return false;
        }

        /// <summary>
        /// すべてのリソースを解放します
        /// </summary>
        public void ReleaseAll()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ResourceManager<TKey, TResource>));

            if (_resourceCleanup != null)
            {
                foreach (var entry in _resources.Values)
                {
                    _resourceCleanup(entry.Resource);
                }
            }

            _resources.Clear();
        }

        /// <summary>
        /// 指定したキーのリソースが存在するかどうかを確認します
        /// </summary>
        /// <param name="key">リソースのキー</param>
        /// <returns>リソースが存在する場合はtrue</returns>
        public bool Contains(TKey key)
        {
            return _resources.ContainsKey(key);
        }

        /// <summary>
        /// 管理しているリソースの数を取得します
        /// </summary>
        public int Count => _resources.Count;

        /// <summary>
        /// 管理しているリソースのキーの一覧を取得します
        /// </summary>
        /// <returns>キーの一覧</returns>
        public ICollection<TKey> GetKeys()
        {
            return _resources.Keys;
        }

        /// <summary>
        /// クリーンアップコールバック
        /// </summary>
        private async void CleanupCallback(object state)
        {
            if (_isDisposed)
                return;

            // 同時に複数のクリーンアップが実行されないようにロック
            if (!await _cleanupLock.WaitAsync(0))
                return;

            try
            {
                var now = DateTime.UtcNow;
                var keysToRemove = new List<TKey>();

                // タイムアウトしたリソースを特定
                foreach (var pair in _resources)
                {
                    var timeSinceLastAccess = now - pair.Value.LastAccessed;
                    if (timeSinceLastAccess > _resourceTimeout)
                    {
                        keysToRemove.Add(pair.Key);
                    }
                }

                // タイムアウトしたリソースを解放
                foreach (var key in keysToRemove)
                {
                    Release(key);
                }
            }
            catch (Exception ex)
            {
                // クリーンアップ中のエラーをログに記録
                Console.Error.WriteLine($"リソースクリーンアップ中にエラーが発生しました: {ex}");
            }
            finally
            {
                _cleanupLock.Release();
            }
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースを解放するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                _cleanupTimer.Dispose();
                _cleanupLock.Dispose();
                ReleaseAll();
            }

            _isDisposed = true;
        }

        /// <summary>
        /// リソースエントリを表すクラス
        /// </summary>
        private class ResourceEntry
        {
            /// <summary>
            /// リソース
            /// </summary>
            public TResource Resource { get; set; }

            /// <summary>
            /// 作成日時
            /// </summary>
            public DateTime Created { get; set; }

            /// <summary>
            /// 最終アクセス日時
            /// </summary>
            public DateTime LastAccessed { get; set; }
        }
    }
}