using System;
using System.Threading;
using System.Threading.Tasks;

namespace MachineLog.Common.Synchronization
{
    /// <summary>
    /// 非同期操作のためのロック機構を提供するクラス
    /// </summary>
    public class AsyncLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly Task<IDisposable> _releaser;
        private bool _isDisposed;

        /// <summary>
        /// AsyncLockを初期化します
        /// </summary>
        public AsyncLock()
        {
            _releaser = Task.FromResult<IDisposable>(new Releaser(this));
        }

        /// <summary>
        /// ロックを非同期に取得します
        /// </summary>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>ロックを解放するためのIDisposableオブジェクト</returns>
        public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return _releaser.Result;
        }

        /// <summary>
        /// ロックを同期的に取得します
        /// </summary>
        /// <returns>ロックを解放するためのIDisposableオブジェクト</returns>
        public IDisposable Lock()
        {
            _semaphore.Wait();
            return _releaser.Result;
        }

        /// <summary>
        /// ロックを解放するためのクラス
        /// </summary>
        private class Releaser : IDisposable
        {
            private readonly AsyncLock _toRelease;
            private bool _isDisposed;

            internal Releaser(AsyncLock toRelease)
            {
                _toRelease = toRelease;
            }

            public void Dispose()
            {
                if (_isDisposed)
                    return;

                _toRelease._semaphore.Release();
                _isDisposed = true;
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
                _semaphore.Dispose();
            }

            _isDisposed = true;
        }
    }
}