using MachineLog.Collector.Models;
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
public class IoTHubService : IIoTHubService, IDisposable
{
  private readonly ILogger<IoTHubService> _logger;
  private readonly IoTHubConfig _config;
  private DeviceClient? _deviceClient;
  private ConnectionState _connectionState;
  private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
  private readonly AsyncRetryPolicy _retryPolicy;
  private bool _disposed;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="config">IoT Hub設定</param>
  public IoTHubService(
      ILogger<IoTHubService> logger,
      IOptions<IoTHubConfig> config)
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
    var result = new ConnectionResult
    {
      Success = false
    };

    var stopwatch = Stopwatch.StartNew();

    try
    {
      // 同時接続を防ぐためのロック
      await _connectionLock.WaitAsync(cancellationToken);

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
        _deviceClient = await CreateDeviceClientAsync(cancellationToken);

        // 接続状態変更ハンドラを設定
        _deviceClient.SetConnectionStatusChangesHandler(ConnectionStatusChangesHandler);

        // 接続を開く
        await _retryPolicy.ExecuteAsync(async (ct) =>
            await _deviceClient.OpenAsync(ct), cancellationToken);

        _connectionState = ConnectionState.Connected;
        result.Success = true;
        _logger.LogInformation("IoT Hubに接続しました: {DeviceId}", _config.DeviceId);
      }
      finally
      {
        _connectionLock.Release();
      }
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
    if (_deviceClient == null || _connectionState == ConnectionState.Disconnected)
    {
      _logger.LogWarning("切断を試みましたが、接続されていません: {DeviceId}", _config.DeviceId);
      return;
    }

    try
    {
      await _connectionLock.WaitAsync(cancellationToken);

      try
      {
        _logger.LogInformation("IoT Hubから切断します: {DeviceId}", _config.DeviceId);
        _connectionState = ConnectionState.Disconnecting;

        // 接続を閉じる
        await _retryPolicy.ExecuteAsync(async (ct) =>
            await _deviceClient.CloseAsync(ct), cancellationToken);

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
        var connectionResult = await ConnectAsync(cancellationToken);
        if (!connectionResult.Success)
        {
          throw new InvalidOperationException("IoT Hubに接続できませんでした");
        }
      }

      // ファイルをアップロード
      using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
      {
        await _retryPolicy.ExecuteAsync(async (ct) =>
            await UploadFileToIoTHubAsync(fileStream, blobName, ct),
            cancellationToken);
      }

      result.Success = true;
      _logger.LogInformation("ファイルのアップロードが完了しました: {FilePath} -> {BlobName}, サイズ: {Size} バイト",
          filePath, blobName, result.FileSizeBytes);
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
    return _connectionState;
  }

  /// <summary>
  /// DeviceClientを作成します
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>DeviceClient</returns>
  protected virtual async Task<DeviceClient> CreateDeviceClientAsync(CancellationToken cancellationToken)
  {
    DeviceClient deviceClient;

    // 認証方法に応じてDeviceClientを作成
    if (!string.IsNullOrEmpty(_config.SasToken))
    {
      // SASトークンを使用した認証
      _logger.LogInformation("SASトークンを使用してDeviceClientを作成します: {DeviceId}", _config.DeviceId);
      deviceClient = DeviceClient.Create(
          GetHostNameFromConnectionString(_config.ConnectionString),
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

    // 非同期メソッドのため、ダミーのタスクを返す
    await Task.CompletedTask;

    return deviceClient;
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
    await _deviceClient.UploadToBlobAsync(uploadPath, fileStream);
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
      await ConnectAsync();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "自動再接続中にエラーが発生しました");
    }
  }

  /// <summary>
  /// 接続文字列からホスト名を取得します
  /// </summary>
  /// <param name="connectionString">接続文字列</param>
  /// <returns>ホスト名</returns>
  private string GetHostNameFromConnectionString(string connectionString)
  {
    var parts = connectionString.Split(';');
    foreach (var part in parts)
    {
      var keyValue = part.Split('=', 2);
      if (keyValue.Length == 2 && keyValue[0].Trim().Equals("HostName", StringComparison.OrdinalIgnoreCase))
      {
        return keyValue[1].Trim();
      }
    }
    throw new ArgumentException("接続文字列にHostNameが含まれていません", nameof(connectionString));
  }

  /// <summary>
  /// リソースを破棄します
  /// </summary>
  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// リソースを破棄します
  /// </summary>
  /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
  protected virtual void Dispose(bool disposing)
  {
    if (_disposed)
    {
      return;
    }

    if (disposing)
    {
      // マネージドリソースの破棄
      if (_deviceClient != null)
      {
        try
        {
          _deviceClient.Dispose();
          _deviceClient = null;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "DeviceClientの破棄中にエラーが発生しました");
        }
      }

      _connectionLock.Dispose();
    }

    _disposed = true;
  }
}
