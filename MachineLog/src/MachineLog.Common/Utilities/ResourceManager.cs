using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MachineLog.Common.Utilities;

/// <summary>
/// リソース管理を担当するクラス。
/// アプリケーション全体のリソースを追跡し、適切な解放を保証します。
/// </summary>
public sealed class ResourceManager : IDisposable, IAsyncDisposable
{
  private static readonly Lazy<ResourceManager> _instance = new Lazy<ResourceManager>(() => new ResourceManager());
  private readonly ConcurrentDictionary<string, TrackedResource> _resources = new();
  private readonly SemaphoreSlim _resourceLock = new SemaphoreSlim(1, 1);
  private readonly Timer _memoryMonitorTimer;
  private readonly Timer _resourceLeakDetectionTimer;
  private long _memoryThresholdBytes;
  private bool _disposed;
  private ILogger? _logger;

  /// <summary>
  /// ResourceManagerのシングルトンインスタンスを取得します
  /// </summary>
  public static ResourceManager Instance => _instance.Value;

  /// <summary>
  /// 現在追跡中のリソース数を取得します
  /// </summary>
  public int ResourceCount => _resources.Count;

  /// <summary>
  /// 現在のメモリ使用量の上限（バイト単位）を取得または設定します
  /// </summary>
  public long MemoryThresholdBytes
  {
    get => Interlocked.Read(ref _memoryThresholdBytes);
    set => Interlocked.Exchange(ref _memoryThresholdBytes, value);
  }

  /// <summary>
  /// コンストラクタ
  /// </summary>
  private ResourceManager()
  {
    // デフォルトメモリしきい値: 1GB
    _memoryThresholdBytes = 1024L * 1024L * 1024L;

    // メモリ監視タイマーの初期化（1分ごとにメモリチェック）
    _memoryMonitorTimer = new Timer(
        CheckMemoryUsage,
        null,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(1));

    // リソースリーク検出タイマーの初期化（5分ごとに検出）
    _resourceLeakDetectionTimer = new Timer(
        DetectResourceLeaks,
        null,
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(5));
  }

  /// <summary>
  /// リソースマネージャーにロガーを設定します
  /// </summary>
  /// <param name="logger">使用するロガー</param>
  public void SetLogger(ILogger logger)
  {
    _logger = logger;
    _logger.LogInformation("ResourceManager initialized with memory threshold: {Threshold} bytes", _memoryThresholdBytes);
  }

  /// <summary>
  /// リソースを追跡対象に登録します
  /// </summary>
  /// <param name="resource">追跡対象のリソース</param>
  /// <param name="description">リソースの説明</param>
  /// <param name="estimatedSizeBytes">リソースの推定サイズ（バイト単位）</param>
  /// <param name="callerFile">呼び出し元のファイル名</param>
  /// <param name="callerMember">呼び出し元のメンバー名</param>
  /// <param name="callerLineNumber">呼び出し元の行番号</param>
  /// <returns>リソースの追跡ID</returns>
  public string TrackResource(
      IDisposable resource,
      string description,
      long estimatedSizeBytes = 0,
      [CallerFilePath] string callerFile = "",
      [CallerMemberName] string callerMember = "",
      [CallerLineNumber] int callerLineNumber = 0)
  {
    if (resource == null) throw new ArgumentNullException(nameof(resource));
    if (_disposed) throw new ObjectDisposedException(nameof(ResourceManager));

    var id = Guid.NewGuid().ToString();
    var trackedResource = new TrackedResource
    {
      Id = id,
      Resource = resource,
      Description = description,
      EstimatedSizeBytes = estimatedSizeBytes,
      CreationTime = DateTime.UtcNow,
      LastAccessTime = DateTime.UtcNow,
      CallerFile = callerFile,
      CallerMember = callerMember,
      CallerLineNumber = callerLineNumber
    };

    _resources.TryAdd(id, trackedResource);
    _logger?.LogDebug("Resource tracked: {Id} - {Description} ({Size} bytes)", id, description, estimatedSizeBytes);

    return id;
  }

  /// <summary>
  /// 非同期リソースを追跡対象に登録します
  /// </summary>
  /// <param name="resource">追跡対象の非同期リソース</param>
  /// <param name="description">リソースの説明</param>
  /// <param name="estimatedSizeBytes">リソースの推定サイズ（バイト単位）</param>
  /// <param name="callerFile">呼び出し元のファイル名</param>
  /// <param name="callerMember">呼び出し元のメンバー名</param>
  /// <param name="callerLineNumber">呼び出し元の行番号</param>
  /// <returns>リソースの追跡ID</returns>
  public string TrackAsyncResource(
      IAsyncDisposable resource,
      string description,
      long estimatedSizeBytes = 0,
      [CallerFilePath] string callerFile = "",
      [CallerMemberName] string callerMember = "",
      [CallerLineNumber] int callerLineNumber = 0)
  {
    if (resource == null) throw new ArgumentNullException(nameof(resource));
    if (_disposed) throw new ObjectDisposedException(nameof(ResourceManager));

    var id = Guid.NewGuid().ToString();
    var trackedResource = new TrackedResource
    {
      Id = id,
      AsyncResource = resource,
      Description = description,
      EstimatedSizeBytes = estimatedSizeBytes,
      CreationTime = DateTime.UtcNow,
      LastAccessTime = DateTime.UtcNow,
      CallerFile = callerFile,
      CallerMember = callerMember,
      CallerLineNumber = callerLineNumber
    };

    _resources.TryAdd(id, trackedResource);
    _logger?.LogDebug("Async resource tracked: {Id} - {Description} ({Size} bytes)", id, description, estimatedSizeBytes);

    return id;
  }

  /// <summary>
  /// リソースのアクセス時間を更新します
  /// </summary>
  /// <param name="resourceId">リソースの追跡ID</param>
  public void UpdateResourceAccess(string resourceId)
  {
    if (string.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
    if (_disposed) throw new ObjectDisposedException(nameof(ResourceManager));

    if (_resources.TryGetValue(resourceId, out var resource))
    {
      resource.LastAccessTime = DateTime.UtcNow;
      resource.AccessCount++;
    }
  }

  /// <summary>
  /// リソースを追跡対象から削除し、解放します
  /// </summary>
  /// <param name="resourceId">リソースの追跡ID</param>
  /// <returns>解放に成功したかどうか</returns>
  public bool ReleaseResource(string resourceId)
  {
    if (string.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
    if (_disposed) throw new ObjectDisposedException(nameof(ResourceManager));

    if (_resources.TryRemove(resourceId, out var resource))
    {
      try
      {
        if (resource.Resource != null)
        {
          resource.Resource.Dispose();
          _logger?.LogDebug("Resource released: {Id} - {Description}", resourceId, resource.Description);
        }
        else if (resource.AsyncResource != null)
        {
          // 非同期リソースの場合、同期的に解放
          try
          {
            // 非同期解放メソッドを同期的に呼び出す
            // 注意: これは非推奨だが、Dispose内では許容される
            resource.AsyncResource.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _logger?.LogDebug("Async resource released synchronously: {Id} - {Description}", resourceId, resource.Description);
          }
          catch (Exception ex)
          {
            _logger?.LogWarning(ex, "Error releasing async resource synchronously: {Id} - {Description}", resourceId, resource.Description);
          }
        }
        return true;
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "Error releasing resource: {Id} - {Description}", resourceId, resource.Description);
        return false;
      }
    }

    return false;
  }

  /// <summary>
  /// リソースを追跡対象から削除し、非同期で解放します
  /// </summary>
  /// <param name="resourceId">リソースの追跡ID</param>
  /// <returns>解放に成功したかどうかを示す非同期タスク</returns>
  public async Task<bool> ReleaseResourceAsync(string resourceId)
  {
    if (string.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
    if (_disposed) throw new ObjectDisposedException(nameof(ResourceManager));

    if (_resources.TryRemove(resourceId, out var resource))
    {
      try
      {
        if (resource.AsyncResource != null)
        {
          await resource.AsyncResource.DisposeAsync().ConfigureAwait(false);
          _logger?.LogDebug("Async resource released: {Id} - {Description}", resourceId, resource.Description);
        }
        else if (resource.Resource != null)
        {
          resource.Resource.Dispose();
          _logger?.LogDebug("Resource released asynchronously: {Id} - {Description}", resourceId, resource.Description);
        }
        return true;
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "Error releasing resource asynchronously: {Id} - {Description}", resourceId, resource.Description);
        return false;
      }
    }

    return false;
  }

  /// <summary>
  /// 指定した時間よりも長く未使用のリソースを解放します
  /// </summary>
  /// <param name="idleThreshold">アイドル時間のしきい値</param>
  /// <returns>解放されたリソースの数</returns>
  public int ReleaseIdleResources(TimeSpan idleThreshold)
  {
    if (_disposed) throw new ObjectDisposedException(nameof(ResourceManager));

    var now = DateTime.UtcNow;
    var resourceIds = _resources
        .Where(r => now - r.Value.LastAccessTime > idleThreshold)
        .Select(r => r.Key)
        .ToList();

    int releasedCount = 0;
    foreach (var id in resourceIds)
    {
      if (ReleaseResource(id))
      {
        releasedCount++;
      }
    }

    if (releasedCount > 0)
    {
      _logger?.LogInformation("Released {Count} idle resources (idle > {Threshold}s)", releasedCount, idleThreshold.TotalSeconds);
    }

    return releasedCount;
  }

  /// <summary>
  /// 指定したサイズ以上のリソースを解放してメモリを確保します
  /// </summary>
  /// <param name="bytesToFree">解放するメモリ量（バイト単位）</param>
  /// <returns>解放されたリソースの数</returns>
  public int FreeMemory(long bytesToFree)
  {
    if (_disposed) throw new ObjectDisposedException(nameof(ResourceManager));
    if (bytesToFree <= 0) throw new ArgumentOutOfRangeException(nameof(bytesToFree), "Bytes to free must be positive");

    // まず、アイドル状態のリソースを解放
    int releasedCount = ReleaseIdleResources(TimeSpan.FromMinutes(5));
    long estimatedFreedBytes = 0;

    // それでも足りない場合は、最終アクセス時間の古いリソースから順に解放
    if (estimatedFreedBytes < bytesToFree)
    {
      var resourcesToRelease = _resources
          .OrderBy(r => r.Value.LastAccessTime)
          .ThenByDescending(r => r.Value.EstimatedSizeBytes)
          .Select(r => r.Key)
          .ToList();

      foreach (var id in resourcesToRelease)
      {
        if (_resources.TryGetValue(id, out var resource))
        {
          if (ReleaseResource(id))
          {
            releasedCount++;
            estimatedFreedBytes += resource.EstimatedSizeBytes;

            if (estimatedFreedBytes >= bytesToFree)
            {
              break;
            }
          }
        }
      }
    }

    _logger?.LogInformation("Freed approximately {Bytes} bytes by releasing {Count} resources", estimatedFreedBytes, releasedCount);
    return releasedCount;
  }

  /// <summary>
  /// すべてのリソースを解放します
  /// </summary>
  /// <returns>解放されたリソースの数</returns>
  public int ReleaseAllResources()
  {
    if (_disposed) throw new ObjectDisposedException(nameof(ResourceManager));

    var resourceIds = _resources.Keys.ToList();
    int releasedCount = 0;

    foreach (var id in resourceIds)
    {
      if (ReleaseResource(id))
      {
        releasedCount++;
      }
    }

    _logger?.LogInformation("Released all {Count} resources", releasedCount);
    return releasedCount;
  }

  /// <summary>
  /// すべてのリソースを非同期で解放します
  /// </summary>
  /// <returns>解放されたリソースの数を示す非同期タスク</returns>
  public async Task<int> ReleaseAllResourcesAsync()
  {
    if (_disposed) throw new ObjectDisposedException(nameof(ResourceManager));

    var resourceIds = _resources.Keys.ToList();
    int releasedCount = 0;

    // リソースを解放するタスクを作成
    var releaseTasks = new List<Task<bool>>();
    foreach (var id in resourceIds)
    {
      releaseTasks.Add(ReleaseResourceAsync(id));
    }

    // すべてのタスクの完了を待機
    var results = await Task.WhenAll(releaseTasks).ConfigureAwait(false);
    releasedCount = results.Count(r => r);

    _logger?.LogInformation("Asynchronously released all {Count} resources", releasedCount);
    return releasedCount;
  }

  /// <summary>
  /// 現在追跡中のすべてのリソースの詳細を取得します
  /// </summary>
  /// <returns>リソース詳細のリスト</returns>
  public IReadOnlyList<ResourceInfo> GetTrackedResources()
  {
    if (_disposed) throw new ObjectDisposedException(nameof(ResourceManager));

    return _resources.Values.Select(r => new ResourceInfo
    {
      Id = r.Id,
      Description = r.Description,
      CreationTime = r.CreationTime,
      LastAccessTime = r.LastAccessTime,
      AccessCount = r.AccessCount,
      EstimatedSizeBytes = r.EstimatedSizeBytes,
      IsAsyncResource = r.AsyncResource != null,
      CallerFile = r.CallerFile,
      CallerMember = r.CallerMember,
      CallerLineNumber = r.CallerLineNumber
    }).ToList();
  }

  /// <summary>
  /// 潜在的なリソースリークを検出します
  /// </summary>
  private void DetectResourceLeaks(object? state)
  {
    try
    {
      if (_disposed) return;

      var now = DateTime.UtcNow;
      var longLivedResources = _resources.Values
          .Where(r => now - r.CreationTime > TimeSpan.FromHours(1))
          .OrderByDescending(r => r.CreationTime)
          .ToList();

      if (longLivedResources.Count > 0)
      {
        _logger?.LogWarning("Detected {Count} potential resource leaks (resources alive > 1 hour)", longLivedResources.Count);

        // 詳細なリーク情報を記録（最大10個まで）
        foreach (var resource in longLivedResources.Take(10))
        {
          _logger?.LogWarning(
              "Potential leaked resource: {Id} - {Description}, Created: {CreationTime}, Last Access: {LastAccessTime}, " +
              "Access Count: {AccessCount}, Location: {CallerFile}:{CallerLineNumber} in {CallerMember}",
              resource.Id, resource.Description, resource.CreationTime, resource.LastAccessTime,
              resource.AccessCount, resource.CallerFile, resource.CallerLineNumber, resource.CallerMember);
        }

        // 明らかに放棄されたリソースを自動解放（1日以上アクセスがないもの）
        var abandonedResources = longLivedResources
            .Where(r => now - r.LastAccessTime > TimeSpan.FromDays(1))
            .Select(r => r.Id)
            .ToList();

        if (abandonedResources.Count > 0)
        {
          int releasedCount = 0;
          foreach (var id in abandonedResources)
          {
            if (ReleaseResource(id))
            {
              releasedCount++;
            }
          }

          _logger?.LogWarning("Auto-released {Count} abandoned resources (no access for > 1 day)", releasedCount);
        }
      }
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Error detecting resource leaks");
    }
  }

  /// <summary>
  /// メモリ使用量をチェックします
  /// </summary>
  private void CheckMemoryUsage(object? state)
  {
    try
    {
      if (_disposed) return;

      var currentProcess = Process.GetCurrentProcess();
      var workingSet = currentProcess.WorkingSet64;
      var privateMemory = currentProcess.PrivateMemorySize64;

      _logger?.LogDebug("Memory usage - Working Set: {WorkingSet} bytes, Private Memory: {PrivateMemory} bytes",
          workingSet, privateMemory);

      // しきい値を超えた場合はメモリを解放
      var threshold = MemoryThresholdBytes;
      if (privateMemory > threshold)
      {
        var bytesToFree = (long)((privateMemory - threshold) * 1.2); // 少し多めに確保
        _logger?.LogWarning("Memory usage ({PrivateMemory} bytes) exceeds threshold ({Threshold} bytes). " +
            "Attempting to free {BytesToFree} bytes", privateMemory, threshold, bytesToFree);

        // メモリ解放を実行
        FreeMemory(bytesToFree);

        // ガベージコレクションを強制実行
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();

        // 解放後のメモリ使用量を記録
        currentProcess = Process.GetCurrentProcess();
        var newPrivateMemory = currentProcess.PrivateMemorySize64;
        _logger?.LogInformation("After memory release: Private Memory: {PrivateMemory} bytes " +
            "(Reduced by {Reduction} bytes)", newPrivateMemory, privateMemory - newPrivateMemory);
      }
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Error checking memory usage");
    }
  }

  /// <summary>
  /// リソースを解放します
  /// </summary>
  public void Dispose()
  {
    if (_disposed) return;

    // リソースの解放
    try
    {
      _memoryMonitorTimer.Dispose();
      _resourceLeakDetectionTimer.Dispose();
      _resourceLock.Dispose();

      // すべてのリソースを解放
      ReleaseAllResources();

      _resources.Clear();
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Error disposing ResourceManager");
    }

    _disposed = true;
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// リソースを非同期で解放します
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;

    // リソースの解放
    try
    {
      _memoryMonitorTimer.Dispose();
      _resourceLeakDetectionTimer.Dispose();

      // すべてのリソースを非同期で解放
      await ReleaseAllResourcesAsync().ConfigureAwait(false);

      _resourceLock.Dispose();
      _resources.Clear();
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Error disposing ResourceManager asynchronously");
    }

    _disposed = true;
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// ファイナライザー
  /// </summary>
  ~ResourceManager()
  {
    try
    {
      // 未解放のリソースがあればログに記録
      if (_resources.Count > 0)
      {
        Debug.WriteLine($"WARNING: ResourceManager finalized with {_resources.Count} active resources!");
        foreach (var resource in _resources.Values.Take(10))
        {
          Debug.WriteLine($"Leaked resource: {resource.Id} - {resource.Description}, " +
              $"Created: {resource.CreationTime}, " +
              $"From: {resource.CallerFile}:{resource.CallerLineNumber}");
        }
      }
    }
    catch
    {
      // ファイナライザー内で例外を投げないようにする
    }
    finally
    {
      // マネージドリソースを解放しない
      Dispose(false);
    }
  }

  /// <summary>
  /// 内部解放メソッド
  /// </summary>
  /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
  private void Dispose(bool disposing)
  {
    if (_disposed) return;

    if (disposing)
    {
      // マネージドリソースの解放（Dispose()で既に処理済み）
    }

    // アンマネージドリソースの解放（この実装では特になし）
    _disposed = true;
  }
}

/// <summary>
/// 追跡対象のリソース情報
/// </summary>
internal class TrackedResource
{
  /// <summary>リソースの一意識別子</summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>同期リソース</summary>
  public IDisposable? Resource { get; set; }

  /// <summary>非同期リソース</summary>
  public IAsyncDisposable? AsyncResource { get; set; }

  /// <summary>リソースの説明</summary>
  public string Description { get; set; } = string.Empty;

  /// <summary>リソースの推定サイズ（バイト単位）</summary>
  public long EstimatedSizeBytes { get; set; }

  /// <summary>リソースの作成時間</summary>
  public DateTime CreationTime { get; set; }

  /// <summary>最終アクセス時間</summary>
  public DateTime LastAccessTime { get; set; }

  /// <summary>アクセス回数</summary>
  public int AccessCount { get; set; }

  /// <summary>呼び出し元のファイル名</summary>
  public string CallerFile { get; set; } = string.Empty;

  /// <summary>呼び出し元のメンバー名</summary>
  public string CallerMember { get; set; } = string.Empty;

  /// <summary>呼び出し元の行番号</summary>
  public int CallerLineNumber { get; set; }
}

/// <summary>
/// 公開するリソース情報
/// </summary>
public class ResourceInfo
{
  /// <summary>リソースの一意識別子</summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>リソースの説明</summary>
  public string Description { get; set; } = string.Empty;

  /// <summary>リソースの推定サイズ（バイト単位）</summary>
  public long EstimatedSizeBytes { get; set; }

  /// <summary>リソースの作成時間</summary>
  public DateTime CreationTime { get; set; }

  /// <summary>最終アクセス時間</summary>
  public DateTime LastAccessTime { get; set; }

  /// <summary>アクセス回数</summary>
  public int AccessCount { get; set; }

  /// <summary>非同期リソースかどうか</summary>
  public bool IsAsyncResource { get; set; }

  /// <summary>呼び出し元のファイル名</summary>
  public string CallerFile { get; set; } = string.Empty;

  /// <summary>呼び出し元のメンバー名</summary>
  public string CallerMember { get; set; } = string.Empty;

  /// <summary>呼び出し元の行番号</summary>
  public int CallerLineNumber { get; set; }
}
