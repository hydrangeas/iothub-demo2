using MachineLog.Collector.Models;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

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
      _logger.LogInformation("IoT Hubに接続を開始します: {DeviceId}", _config.DeviceId);
      _connectionState = ConnectionState.Connecting;

      // DeviceClientを作成
      _deviceClient = await CreateDeviceClientAsync(cancellationToken);

      // 接続を開く
      await _deviceClient.OpenAsync(cancellationToken);

      _connectionState = ConnectionState.Connected;
      result.Success = true;
      _logger.LogInformation("IoT Hubに接続しました: {DeviceId}", _config.DeviceId);
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
      _logger.LogInformation("IoT Hubから切断します: {DeviceId}", _config.DeviceId);
      _connectionState = ConnectionState.Disconnecting;

      // 接続を閉じる
      await _deviceClient.CloseAsync(cancellationToken);
      _deviceClient.Dispose();
      _deviceClient = null;
      _connectionState = ConnectionState.Disconnected;

      _logger.LogInformation("IoT Hubから切断しました: {DeviceId}", _config.DeviceId);
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
        await UploadFileToIoTHubAsync(fileStream, blobName, cancellationToken);
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
    // 接続文字列からDeviceClientを作成
    var deviceClient = DeviceClient.CreateFromConnectionString(
        _config.ConnectionString,
        TransportType.Mqtt);

    // 実際のプロジェクトでは、ここでファイルアップロード用の設定を行います
    // テスト目的のため、簡略化しています
    await Task.CompletedTask; // 非同期メソッドのため、ダミーのタスクを返す

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

    // アップロード先のパスを構築
    var uploadPath = Path.Combine(_config.UploadFolderPath, blobName);

    // ファイルをアップロード
    // 実際のプロジェクトでは、ここでIoT Hubにファイルをアップロードします
    // テスト目的のため、簡略化しています
    _logger.LogInformation("ファイルをアップロードします: {Path}, サイズ: {Size} バイト", uploadPath, fileStream.Length);

    // アップロードをシミュレート
    await Task.Delay(TimeSpan.FromMilliseconds(fileStream.Length / 1024), cancellationToken); // 1KBあたり1ミリ秒
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
    }
  }
}