using System;
using System.Threading.Tasks;

namespace MachineLog.Common.Utilities;

/// <summary>
/// リソース解放パターンを実装した基底クラス
/// IDisposableとIAsyncDisposableの両方を適切に実装します
/// </summary>
public abstract class DisposableBase : IDisposable, IAsyncDisposable
{
  /// <summary>オブジェクトが破棄されたかどうか</summary>
  protected bool _disposed;

  /// <summary>
  /// ファイナライザー
  /// </summary>
  ~DisposableBase()
  {
    // マネージドリソースを解放しない
    Dispose(false);
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
  /// リソースを非同期で解放します
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    await DisposeAsyncCore().ConfigureAwait(false);

    // マネージドリソースを解放したのでファイナライザーを抑制
    Dispose(false);
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// リソースの解放処理を行います
  /// </summary>
  /// <param name="disposing">マネージドリソースも解放する場合はtrue</param>
  protected virtual void Dispose(bool disposing)
  {
    if (_disposed)
      return;

    if (disposing)
    {
      // マネージドリソースの解放
      ReleaseManagedResources();
    }

    // アンマネージドリソースの解放
    ReleaseUnmanagedResources();

    _disposed = true;
  }

  /// <summary>
  /// 非同期でリソースを解放する内部実装
  /// </summary>
  protected virtual async ValueTask DisposeAsyncCore()
  {
    if (_disposed)
      return;

    // マネージドリソースを非同期で解放
    await ReleaseManagedResourcesAsync().ConfigureAwait(false);

    // アンマネージドリソースを解放
    ReleaseUnmanagedResources();

    _disposed = true;
  }

  /// <summary>
  /// マネージドリソースを解放します
  /// 派生クラスで必要に応じてオーバーライドしてください
  /// </summary>
  protected virtual void ReleaseManagedResources()
  {
    // 派生クラスで実装
  }

  /// <summary>
  /// マネージドリソースを非同期で解放します
  /// 派生クラスで必要に応じてオーバーライドしてください
  /// </summary>
  protected virtual ValueTask ReleaseManagedResourcesAsync()
  {
    // 同期実装をデフォルトとする
    ReleaseManagedResources();
    return ValueTask.CompletedTask;
  }

  /// <summary>
  /// アンマネージドリソースを解放します
  /// 派生クラスで必要に応じてオーバーライドしてください
  /// </summary>
  protected virtual void ReleaseUnmanagedResources()
  {
    // 派生クラスで実装
  }

  /// <summary>
  /// オブジェクトが破棄済みの場合は例外をスローします
  /// </summary>
  protected void ThrowIfDisposed()
  {
    if (_disposed)
    {
      throw new ObjectDisposedException(GetType().Name);
    }
  }
}

/// <summary>
/// リソース解放パターンを実装した非同期リソース専用の基底クラス
/// </summary>
/// <typeparam name="T">派生クラスの型</typeparam>
public abstract class AsyncDisposableBase<T> : DisposableBase where T : AsyncDisposableBase<T>
{
  /// <summary>リソースマネージャーでの追跡ID</summary>
  private string? _resourceTrackingId;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="registerWithResourceManager">リソースマネージャーに登録するかどうか</param>
  protected AsyncDisposableBase(bool registerWithResourceManager = true)
  {
    if (registerWithResourceManager)
    {
      // リソースマネージャーに登録
      _resourceTrackingId = ResourceManager.Instance.TrackAsyncResource(
          this,
          GetType().Name,
          EstimateResourceSize());
    }
  }

  /// <summary>
  /// リソースのサイズを推定します（バイト単位）
  /// 派生クラスで必要に応じてオーバーライドしてください
  /// </summary>
  /// <returns>推定サイズ（バイト単位）</returns>
  protected virtual long EstimateResourceSize()
  {
    return 1024; // デフォルト値
  }

  /// <summary>
  /// マネージドリソースを解放します
  /// </summary>
  protected override void ReleaseManagedResources()
  {
    if (_resourceTrackingId != null)
    {
      // リソーストラッキングの解除はしない
      // ResourceManager側でDisposeが行われるため、循環を防ぐ
      _resourceTrackingId = null;
    }

    base.ReleaseManagedResources();
  }

  /// <summary>
  /// 非同期でマネージドリソースを解放します
  /// </summary>
  protected override async ValueTask ReleaseManagedResourcesAsync()
  {
    if (_resourceTrackingId != null)
    {
      // リソーストラッキングの解除はしない
      // ResourceManager側でDisposeAsyncが行われるため、循環を防ぐ
      _resourceTrackingId = null;
    }

    await base.ReleaseManagedResourcesAsync().ConfigureAwait(false);
  }
}

/// <summary>
/// リソース解放パターンを実装した同期リソース専用の基底クラス
/// </summary>
/// <typeparam name="T">派生クラスの型</typeparam>
public abstract class SyncDisposableBase<T> : DisposableBase where T : SyncDisposableBase<T>
{
  /// <summary>リソースマネージャーでの追跡ID</summary>
  private string? _resourceTrackingId;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="registerWithResourceManager">リソースマネージャーに登録するかどうか</param>
  protected SyncDisposableBase(bool registerWithResourceManager = true)
  {
    if (registerWithResourceManager)
    {
      // リソースマネージャーに登録
      _resourceTrackingId = ResourceManager.Instance.TrackResource(
          this,
          GetType().Name,
          EstimateResourceSize());
    }
  }

  /// <summary>
  /// リソースのサイズを推定します（バイト単位）
  /// 派生クラスで必要に応じてオーバーライドしてください
  /// </summary>
  /// <returns>推定サイズ（バイト単位）</returns>
  protected virtual long EstimateResourceSize()
  {
    return 1024; // デフォルト値
  }

  /// <summary>
  /// マネージドリソースを解放します
  /// </summary>
  protected override void ReleaseManagedResources()
  {
    if (_resourceTrackingId != null)
    {
      // リソーストラッキングの解除はしない
      // ResourceManager側でDisposeが行われるため、循環を防ぐ
      _resourceTrackingId = null;
    }

    base.ReleaseManagedResources();
  }
}
