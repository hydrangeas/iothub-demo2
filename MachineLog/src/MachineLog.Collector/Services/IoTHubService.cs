using MachineLog.Collector.Models;
using MachineLog.Common.Utilities;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Client.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace MachineLog.Collector.Services;

/// <summary>
/// IoT Hubサービスの実装
/// </summary>
public class IoTHubService : AsyncDisposableBase<IoTHubService>, IIoTHubService
{
  private readonly ILogger<IoTHubService> _logger;
  private readonly IoTHubConfig _config;
  private DeviceClient? _deviceClient;
  private ConnectionState _connectionState;
  private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
  private readonly AsyncRetryPolicy _retryPolicy;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="config">IoT Hub設定</param>
  public IoTHubService(
      ILogger<IoTHubService> logger,
      IOptions<IoTHubConfig> config) : base(true) // リソースマネージャーに登録
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    _connectionState = ConnectionState.Disconnected;

    // リトライポリシーの設定
    _retryPolicy = Policy
        .Handle<IotHubException>()
        .Or<TimeoutException>()
        .Or<SocketException>()
        .Or<IOException>()
        .WaitAndRetryAsync(
            5, // 最大リトライ回数
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // 指数バックオフ
            (exception, timeSpan, retryCount, context) =>
            {
              _logger.LogWarning(exception,
                  "IoT Hub操作中にエラーが発生しました。{RetryCount}回目のリトライを{RetryTimeSpan}秒後に実行します。",
                  retryCount, timeSpan.TotalSeconds);
            });
  }

  /// <summary>
  /// IoT Hubに接続します
  /// </summary>
  public virtual async Task<ConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    var result = new ConnectionResult
    {
      Success = false
    };

    var stopwatch = Stopwatch.StartNew();

    try
    {
      // キャンセルされた場合は早期リターン
      if (cancellationToken.IsCancellationRequested)
      {
        _logger.LogWarning("接続がキャンセルされました: {DeviceId}", _config.DeviceId);
        result.ErrorMessage = "接続がキャンセルされました";
        return result;
      }

      // 同時接続を防ぐためのロック
      await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

      try
      {
        // 既に接続済みの場合は何もしない
        if (_connectionState == ConnectionState.Connected && _deviceClient != null)
        {
          _logger.LogInformation("既にIoT Hubに接続されています: {DeviceId}", _config.DeviceId);
          result.Success = true;
          return result;
        }

        _logger.LogInformation("IoT Hubに接続を開始します: {DeviceId}", _config.DeviceId);
        _connectionState = ConnectionState.Connecting;

        // DeviceClientを作成
        _deviceClient = await CreateDeviceClientAsync(cancellationToken).ConfigureAwait(false);

        // 接続状態変更ハンドラを設定
        _deviceClient.SetConnectionStatusChangesHandler(ConnectionStatusChangesHandler);

        // 接続を開く
        await _retryPolicy.ExecuteAsync(async (ct) =>
            await _deviceClient.OpenAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        _connectionState = ConnectionState.Connected;
        result.Success = true;
        _logger.LogInformation("IoT Hubに接続しました: {DeviceId}", _config.DeviceId);
      }
      finally
      {
        _connectionLock.Release();
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // キャンセルは正常な動作
      _logger.LogInformation("IoT Hub接続がキャンセルされました: {DeviceId}", _config.DeviceId);
      result.ErrorMessage = "接続がキャンセルされました";
    }
    catch (Exception ex)
    {
      _connectionState = ConnectionState.Error;
      result.ErrorMessage = ex.Message;
      result.Exception = ex;
      _logger.LogError(ex, "IoT Hubへの接続中にエラーが発生しました: {DeviceId}", _config.DeviceId);
    }
    finally
    {
      stopwatch.Stop();
      result.ConnectionTimeMs = stopwatch.ElapsedMilliseconds;
    }

    return result;
  }

  /// <summary>
  /// IoT Hubから切断します
  /// </summary>
  public virtual async Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    if (_deviceClient == null || _connectionState == ConnectionState.Disconnected)
    {
      _logger.LogWarning("切断を試みましたが、接続されていません: {DeviceId}", _config.DeviceId);
      return;
    }

    try
    {
      // キャンセルされた場合は早期リターン
      if (cancellationToken.IsCancellationRequested)
      {
        _logger.LogWarning("切断処理がキャンセルされました: {DeviceId}", _config.DeviceId);
        return;
      }

      await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

      try
      {
        _logger.LogInformation("IoT Hubから切断します: {DeviceId}", _config.DeviceId);
        _connectionState = ConnectionState.Disconnecting;

        // 接続を閉じる
        await _retryPolicy.ExecuteAsync(async (ct) =>
            await _deviceClient.CloseAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        _deviceClient.Dispose();
        _deviceClient = null;
        _connectionState = ConnectionState.Disconnected;

        _logger.LogInformation("IoT Hubから切断しました: {DeviceId}", _config.DeviceId);
      }
      finally
      {
        _connectionLock.Release();
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // キャンセルは正常な動作
      _logger.LogInformation("IoT Hub切断処理がキャンセルされました: {DeviceId}", _config.DeviceId);
    }
    catch (Exception ex)
    {
      _connectionState = ConnectionState.Error;
      _logger.LogError(ex, "IoT Hubからの切断中にエラーが発生しました: {DeviceId}", _config.DeviceId);
      throw;
    }
  }

  /// <summary>
  /// ファイルをIoT Hubにアップロードします
  /// </summary>
  public virtual async Task<FileUploadResult> UploadFileAsync(string filePath, string blobName, CancellationToken cancellationToken = default)
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    var result = new FileUploadResult
    {
      Success = false,
      FilePath = filePath,
      BlobName = blobName
    };

    var stopwatch = Stopwatch.StartNew();

    try
    {
      _logger.LogInformation("ファイルのアップロードを開始します: {FilePath} -> {BlobName}", filePath, blobName);

      // キャンセルされた場合は早期リターン
      if (cancellationToken.IsCancellationRequested)
      {
        _logger.LogWarning("アップロードがキャンセルされました: {FilePath}", filePath);
        result.ErrorMessage = "アップロードがキャンセルされました";
        return result;
      }

      // ファイルの存在確認
      if (!File.Exists(filePath))
      {
        throw new FileNotFoundException("アップロード対象のファイルが見つかりません", filePath);
      }

      // ファイルサイズを取得
      var fileInfo = new FileInfo(filePath);
      result.FileSizeBytes = fileInfo.Length;

      // 接続状態を確認
      if (_deviceClient == null || _connectionState != ConnectionState.Connected)
      {
        _logger.LogWarning("ファイルアップロード前に接続が確立されていません。接続を試みます。");
        var connectionResult = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        if (!connectionResult.Success)
        {
          throw new InvalidOperationException("IoT Hubに接続できませんでした");
        }
      }

      // キャンセルされた場合は早期リターン
      if (cancellationToken.IsCancellationRequested)
      {
        _logger.LogWarning("アップロードがキャンセルされました: {FilePath}", filePath);
        result.ErrorMessage = "アップロードがキャンセルされました";
        return result;
      }

      // ファイルをアップロード
      using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
      {
        await _retryPolicy.ExecuteAsync(async (ct) =>
            await UploadFileToIoTHubAsync(fileStream, blobName, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
      }

      result.Success = true;
      _logger.LogInformation("ファイルのアップロードが完了しました: {FilePath} -> {BlobName}, サイズ: {Size} バイト",
          filePath, blobName, result.FileSizeBytes);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // キャンセルは正常な動作
      _logger.LogInformation("ファイルアップロードがキャンセルされました: {FilePath}", filePath);
      result.ErrorMessage = "アップロードがキャンセルされました";
    }
    catch (Exception ex)
    {
      result.ErrorMessage = ex.Message;
      result.Exception = ex;
      _logger.LogError(ex, "ファイルのアップロード中にエラーが発生しました: {FilePath} -> {BlobName}", filePath, blobName);
    }
    finally
    {
      stopwatch.Stop();
      result.UploadTimeMs = stopwatch.ElapsedMilliseconds;
    }

    return result;
  }

  /// <summary>
  /// 接続状態を取得します
  /// </summary>
  public virtual ConnectionState GetConnectionState()
  {
    // オブジェクトが破棄済みの場合は例外をスロー
    ThrowIfDisposed();

    return _connectionState;
  }

  /// <summary>
  /// DeviceClientを作成します
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>DeviceClient</returns>
  protected virtual async Task<DeviceClient> CreateDeviceClientAsync(CancellationToken cancellationToken)
  {
    // キャンセルされた場合は早期リターン
    if (cancellationToken.IsCancellationRequested)
    {
      throw new OperationCanceledException("DeviceClient作成がキャンセルされました", cancellationToken);
    }

    DeviceClient deviceClient;

    // 認証方法に応じてDeviceClientを作成
    if (!string.IsNullOrEmpty(_config.SasToken))
    {
      // SASトークンを使用した認証
      _logger.LogInformation("SASトークンを使用してDeviceClientを作成します: {DeviceId}", _config.DeviceId);

      // 接続文字列からホスト名を抽出
      string hostName = ExtractHostNameFromConnectionString(_config.ConnectionString);

      deviceClient = DeviceClient.Create(
          hostName,
          new DeviceAuthenticationWithToken(_config.DeviceId, _config.SasToken),
          TransportType.Mqtt);
    }
    else
    {
      // 接続文字列を使用した認証
      _logger.LogInformation("接続文字列を使用してDeviceClientを作成します: {DeviceId}", _config.DeviceId);
      deviceClient = DeviceClient.CreateFromConnectionString(
          _config.ConnectionString,
          TransportType.Mqtt);
    }

    // DeviceClientのオプション設定
    deviceClient.SetConnectionStatusChangesHandler(ConnectionStatusChangesHandler);
    deviceClient.SetRetryPolicy(new ExponentialBackoff(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1)));
    deviceClient.OperationTimeoutInMilliseconds = (uint)TimeSpan.FromMinutes(2).TotalMilliseconds;

    // 非同期メソッドのため、ダミーのタスクを返す
    // ConfigureAwait(false)を使用して同期コンテキストを最適化
    await Task.CompletedTask.ConfigureAwait(false);

    return deviceClient;
  }

  /// <summary>
  /// 接続文字列からホスト名を抽出します
  /// </summary>
  /// <param name="connectionString">接続文字列</param>
  /// <returns>ホスト名</returns>
  private string ExtractHostNameFromConnectionString(string connectionString)
  {
    if (string.IsNullOrEmpty(connectionString))
    {
      throw new ArgumentException("接続文字列が指定されていません", nameof(connectionString));
    }

    // 接続文字列からHostNameを抽出
    var parts = connectionString.Split(';');
    foreach (var part in parts)
    {
      if (part.StartsWith("HostName=", StringComparison.OrdinalIgnoreCase))
      {
        return part.Substring("HostName=".Length);
      }
    }

    throw new ArgumentException("接続文字列にHostNameが含まれていません", nameof(connectionString));
  }

  /// <summary>
  /// ファイルをIoT Hubにアップロードします
  /// </summary>
  /// <param name="fileStream">ファイルストリーム</param>
  /// <param name="blobName">Blobの名前</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>タスク</returns>
  protected virtual async Task UploadFileToIoTHubAsync(Stream fileStream, string blobName, CancellationToken cancellationToken)
  {
    if (_deviceClient == null)
    {
      throw new InvalidOperationException("DeviceClientが初期化されていません");
    }

    // キャンセルされた場合は早期リターン
    if (cancellationToken.IsCancellationRequested)
    {
      throw new OperationCanceledException("ファイルアップロードがキャンセルされました", cancellationToken);
    }

    // 現在の日付に基づいてフォルダ構造を作成 (yyyy/MM/dd/{machineId})
    var today = DateTime.UtcNow;
    var folderPath = Path.Combine(
        _config.UploadFolderPath,
        today.Year.ToString(),
        today.Month.ToString("00"),
        today.Day.ToString("00"),
        _config.DeviceId);

    // アップロード先のパスを構築
    var uploadPath = Path.Combine(folderPath, blobName);
    _logger.LogInformation("ファイルをアップロードします: {Path}, サイズ: {Size} バイト", uploadPath, fileStream.Length);

    // UploadToBlobAsyncを使用（問題が解決するまで一時的対応）
    // 注意: Azure IoT SDK内部でConfigureAwait(false)が使用されていない可能性があるため、
    // 呼び出し元でConfigureAwait(false)を使用することで、デッドロックのリスクを軽減
    await _deviceClient.UploadToBlobAsync(uploadPath, fileStream, cancellationToken).ConfigureAwait(false);
    _logger.LogInformation("ファイルのアップロードが完了しました: {Path}", uploadPath);
  }

  /// <summary>
  /// 接続状態変更ハンドラ
  /// </summary>
  /// <param name="status">接続状態</param>
  /// <param name="reason">変更理由</param>
  private void ConnectionStatusChangesHandler(ConnectionStatus status, ConnectionStatusChangeReason reason)
  {
    _logger.LogInformation("IoT Hub接続状態が変更されました: {Status}, 理由: {Reason}", status, reason);

    switch (status)
    {
      case ConnectionStatus.Connected:
        _connectionState = ConnectionState.Connected;
        break;
      case ConnectionStatus.Disconnected:
        _connectionState = ConnectionState.Disconnected;
        break;
      case ConnectionStatus.Disabled:
        _connectionState = ConnectionState.Error;
        break;
      default:
        // 未知の接続状態の場合はエラー状態に設定
        _logger.LogWarning("未知の接続状態が検出されました: {Status}", status);
        _connectionState = ConnectionState.Error;
        break;
    }

    // 一時的なエラーの場合は自動的に再接続を試みる
    if (status == ConnectionStatus.Disconnected &&
        (reason == ConnectionStatusChangeReason.Communication_Error ||
         reason == ConnectionStatusChangeReason.Connection_Ok))
    {
      _logger.LogWarning("一時的な接続エラーが発生しました。再接続を試みます。");
      _ = HandleReconnectionAsync();
    }
  }

  /// <summary>
  /// 再接続処理を行います
  /// </summary>
  /// <returns>タスク</returns>
  private async Task HandleReconnectionAsync()
  {
    try
    {
      // 再接続のためのキャンセルトークンを作成（タイムアウト付き）
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
      await ConnectAsync(cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // タイムアウトは正常
      _logger.LogWarning("自動再接続がタイムアウトしました");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "自動再接続中にエラーが発生しました");
    }
  }

  /// <summary>
  /// マネージドリソースを解放します
  /// </summary>
  protected override void ReleaseManagedResources()
  {
    _logger.LogInformation("IoTHubServiceのリソースを解放します");

    try
    {
      if (_deviceClient != null)
      {
        // デバイスクライアントを安全に解放
        // 注意: 同期的な処理のため、非同期操作はタイムアウト付きで実行
        try
        {
          // 同期処理なため、5秒のタイムアウトを設定
          using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
          var closeTask = _deviceClient.CloseAsync(cts.Token);

          // GetAwaiter().GetResult()を使用して同期的に待機し、例外を適切に伝播
          try
          {
            _deviceClient.CloseAsync(cts.Token).GetAwaiter().GetResult();
          }
          catch (OperationCanceledException)
          {
            _logger.LogWarning("IoT Hubクライアントの切断がタイムアウトしました");
          }
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "IoT Hubクライアントの切断中にエラーが発生しました");
        }

        _deviceClient.Dispose();
        _deviceClient = null;
      }

      // 接続ロックを解放
      _connectionLock.Dispose();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "IoTHubServiceのリソース解放中にエラーが発生しました");
    }

    _connectionState = ConnectionState.Disconnected;

    // 基底クラスのリソース解放を呼び出す
    base.ReleaseManagedResources();
  }

  /// <summary>
  /// リソースのサイズを推定します
  /// </summary>
  /// <returns>推定サイズ（バイト単位）</returns>
  protected override long EstimateResourceSize()
  {
    // DeviceClientは比較的大きなリソースとして扱う
    return 5 * 1024 * 1024; // 5MB
  }

  /// <summary>
  /// マネージドリソースを非同期で解放します
  /// </summary>
  protected override async ValueTask ReleaseManagedResourcesAsync()
  {
    _logger.LogInformation("IoTHubServiceのリソースを非同期で解放します");

    try
    {
      // 接続を閉じる
      if (_deviceClient != null && _connectionState == ConnectionState.Connected)
      {
        try
        {
          // 非同期で接続を閉じる
          await _deviceClient.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "IoT Hub接続の非同期切断中にエラーが発生しました");
        }
      }

      // DeviceClientを破棄
      if (_deviceClient != null)
      {
        _deviceClient.Dispose();
        _deviceClient = null;
      }

      // 接続ロックを解放
      _connectionLock.Dispose();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "IoTHubServiceのリソース非同期解放中にエラーが発生しました");
    }

    _connectionState = ConnectionState.Disconnected;

    // 基底クラスのリソース解放を呼び出す
    await base.ReleaseManagedResourcesAsync().ConfigureAwait(false);
  }
}
