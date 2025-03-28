using MachineLog.Collector.Models;
using MachineLog.Collector.Utilities;
using MachineLog.Common.Exceptions;
using MachineLog.Common.Logging;
using MachineLog.Common.Utilities;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Client.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Azure.Storage.Blobs.Models; // BlobHttpHeadersのために追加

namespace MachineLog.Collector.Services;

/// <summary>
/// IoT Hubサービスの実装
/// </summary>
public class IoTHubService : AsyncDisposableBase<IoTHubService>, IIoTHubService
{
  // 定数の定義
  private const int DefaultIoTHubOperationTimeoutMinutes = 2;
  private const int DefaultRetryCount = 5;
  private const double DefaultInitialBackoffSeconds = 1.0;
  private const int DisconnectTimeoutSeconds = 5;
  private const int ReconnectTimeoutSeconds = 30;
  private const string DefaultContentType = "application/octet-stream";
  private const string JsonContentType = "application/json";
  private const string PlainTextContentType = "text/plain";

  private readonly ILogger<IoTHubService> _logger;
  private readonly IoTHubConfig _config;
  private DeviceClient? _deviceClient;
  private ConnectionState _connectionState;
  private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
  private readonly StructuredLogger _structuredLogger;
  private readonly RetryHandler _retryHandler;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="logger">ロガー</param>
  /// <param name="config">IoT Hub設定</param>
  /// <param name="structuredLoggerFactory">構造化ロガーファクトリ</param>
  /// <param name="retryHandlerFactory">リトライハンドラファクトリ</param>
  public IoTHubService(
      ILogger<IoTHubService> logger,
      IOptions<IoTHubConfig> config,
      Func<ILogger, StructuredLogger> structuredLoggerFactory,
      Func<ILogger, RetryHandler> retryHandlerFactory) : base(true) // リソースマネージャーに登録
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    _connectionState = ConnectionState.Disconnected;

    // 構造化ロガーとリトライハンドラを初期化
    _structuredLogger = structuredLoggerFactory?.Invoke(logger) ?? throw new ArgumentNullException(nameof(structuredLoggerFactory));
    _retryHandler = retryHandlerFactory?.Invoke(logger) ?? throw new ArgumentNullException(nameof(retryHandlerFactory));
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

        // キャンセルの再確認
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("IoT Hubに接続を開始します: {DeviceId}", _config.DeviceId);
        _connectionState = ConnectionState.Connecting;

        // DeviceClientを作成
        _deviceClient = await CreateDeviceClientAsync(cancellationToken).ConfigureAwait(false);

        // 接続状態変更ハンドラを設定
        _deviceClient.SetConnectionStatusChangesHandler(ConnectionStatusChangesHandler);

        // 接続を開く（RetryHandlerを使用）
        await OpenConnectionWithRetryAsync(cancellationToken).ConfigureAwait(false);

        _connectionState = ConnectionState.Connected;
        result.Success = true;
        _logger.LogInformation("IoT Hubに接続しました: {DeviceId}", _config.DeviceId);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        // キャンセルは正常な動作
        _logger.LogInformation("IoT Hub接続がキャンセルされました: {DeviceId}", _config.DeviceId);
        result.ErrorMessage = "接続がキャンセルされました";
        throw; // 外側のcatchで処理するために再スロー
      }
      catch (Exception ex)
      {
        _connectionState = ConnectionState.Error;
        result.ErrorMessage = ex.Message;
        result.Exception = ex;
        _logger.LogError(ex, "IoT Hubへの接続中にエラーが発生しました: {DeviceId}", _config.DeviceId);
        throw; // 外側のcatchで処理するために再スロー
      }
      finally
      {
        // ロックを確実に解放
        if (_connectionLock.CurrentCount == 0)
        {
          _connectionLock.Release();
        }
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // キャンセルは正常な動作
      // 既にログは内側でとっているのでここでは何もしない
    }
    catch (Exception)
    {
      // 例外は上層catchブロックで処理済み
    }
    finally
    {
      stopwatch.Stop();
      result.ConnectionTimeMs = stopwatch.ElapsedMilliseconds;
    }

    return result;
  }

  /// <summary>
  /// リトライ機能を使用してIoT Hub接続を開きます
  /// </summary>
  private async Task OpenConnectionWithRetryAsync(CancellationToken cancellationToken)
  {
    if (_deviceClient == null)
    {
      throw new InvalidOperationException("DeviceClientが初期化されていません");
    }

    var context = new Dictionary<string, object> { ["Operation"] = "OpenAsync" };
    await _retryHandler.ExecuteWithRetryAsync(
        $"IoTHubConnect_{_config.DeviceId}",
        async (ct) =>
        {
          await _deviceClient.OpenAsync(ct).ConfigureAwait(false);
          _logger.LogDebug("IoT Hub接続が確立されました: {DeviceId}", _config.DeviceId);
        },
        MachineLog.Common.Utilities.RetryPolicy.CreateIoTHubRetryPolicy(_logger, "IoTHubConnect", DefaultRetryCount, DefaultInitialBackoffSeconds),
        context,
        cancellationToken).ConfigureAwait(false);
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

      // 同時操作を防ぐためのロック
      await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

      try
      {
        _logger.LogInformation("IoT Hubから切断します: {DeviceId}", _config.DeviceId);
        _connectionState = ConnectionState.Disconnecting;

        // 接続を閉じる（RetryHandlerを使用）
        var context = new Dictionary<string, object> { ["Operation"] = "CloseAsync" };
        await _retryHandler.ExecuteWithRetryAsync(
            $"IoTHubDisconnect_{_config.DeviceId}",
            async (ct) =>
            {
              await _deviceClient.CloseAsync(ct).ConfigureAwait(false);
              _logger.LogDebug("IoT Hub接続が閉じられました: {DeviceId}", _config.DeviceId);
            },
            MachineLog.Common.Utilities.RetryPolicy.CreateIoTHubRetryPolicy(_logger, "IoTHubDisconnect", DefaultRetryCount, DefaultInitialBackoffSeconds), // ポリシーを都度生成
            context,
            cancellationToken).ConfigureAwait(false);

        // デバイスクライアントを安全に解放
        ResourceUtility.SafeDispose(_logger, _deviceClient, "IoT Hubクライアント");
        _deviceClient = null;
        _connectionState = ConnectionState.Disconnected;

        _logger.LogInformation("IoT Hubから切断しました: {DeviceId}", _config.DeviceId);
      }
      finally
      {
        // ロックを確実に解放
        if (_connectionLock.CurrentCount == 0)
        {
          _connectionLock.Release();
        }
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
      // 例外を再スローせず、エラーをログに記録するだけにする
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

      // 入力パラメータの検証と前処理
      FileInfo? fileInfo = await ValidateAndPrepareFileAsync(filePath, result, cancellationToken).ConfigureAwait(false);
      if (fileInfo == null)
      {
        return result; // 前処理で問題があった場合は早期リターン
      }

      result.FileSizeBytes = fileInfo.Length;

      // 接続状態を確認し、必要に応じて接続
      if (!await EnsureConnectedAsync(filePath, result, cancellationToken).ConfigureAwait(false))
      {
        return result; // 接続に問題があった場合は早期リターン
      }

      // キャンセルの再確認
      if (cancellationToken.IsCancellationRequested)
      {
        _logger.LogWarning("アップロードがキャンセルされました: {FilePath}", filePath);
        result.ErrorMessage = "アップロードがキャンセルされました";
        return result;
      }

      // ファイルをアップロード
      await UploadFileWithRetryAsync(filePath, fileInfo, blobName, result, cancellationToken).ConfigureAwait(false);

      if (!result.Success)
      {
        return result; // アップロード中に問題が発生した場合
      }

      _logger.LogInformation("ファイルのアップロードが完了しました: {FilePath} -> {BlobName}, サイズ: {Size:N0} バイト",
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
  /// ファイルの存在確認と前処理を行います
  /// </summary>
  private async Task<FileInfo?> ValidateAndPrepareFileAsync(string filePath, FileUploadResult result, CancellationToken cancellationToken)
  {
    // キャンセルされた場合は早期リターン
    if (cancellationToken.IsCancellationRequested)
    {
      _logger.LogWarning("アップロードがキャンセルされました: {FilePath}", filePath);
      result.ErrorMessage = "アップロードがキャンセルされました";
      return null;
    }

    // ファイルの存在確認
    if (!File.Exists(filePath))
    {
      var ex = new FileNotFoundException("アップロード対象のファイルが見つかりません", filePath);
      _logger.LogError(ex, "ファイルが見つかりません: {FilePath}", filePath);
      result.ErrorMessage = ex.Message;
      result.Exception = ex;
      return null;
    }

    // ファイル情報を取得
    var fileInfo = new FileInfo(filePath);

    // キャンセル対応のため、非同期で処理を続行
    await Task.CompletedTask.ConfigureAwait(false);

    return fileInfo;
  }

  /// <summary>
  /// IoT Hubへの接続が確立されていることを確認します
  /// </summary>
  private async Task<bool> EnsureConnectedAsync(string filePath, FileUploadResult result, CancellationToken cancellationToken)
  {
    // 接続状態を確認し、必要に応じて接続
    if (_deviceClient == null || _connectionState != ConnectionState.Connected)
    {
      _logger.LogWarning("ファイルアップロード前に接続が確立されていません。接続を試みます。");
      var connectionResult = await ConnectAsync(cancellationToken).ConfigureAwait(false);
      if (!connectionResult.Success)
      {
        var ex = new InvalidOperationException("IoT Hubに接続できませんでした");
        _logger.LogError(ex, "ファイルアップロード前の接続に失敗しました: {FilePath}", filePath);
        result.ErrorMessage = ex.Message;
        result.Exception = ex;
        return false;
      }
    }
    return true;
  }

  /// <summary>
  /// リトライ機能を使用してファイルをアップロードします
  /// </summary>
  private async Task UploadFileWithRetryAsync(string filePath, FileInfo fileInfo, string blobName, FileUploadResult result, CancellationToken cancellationToken)
  {
    try
    {
      using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
      {
        // リトライ用のコンテキスト情報
        var context = new Dictionary<string, object>
        {
          ["FilePath"] = filePath,
          ["BlobName"] = blobName,
          ["FileSize"] = fileInfo.Length
        };

        // リトライハンドラを使用してアップロード
        await _retryHandler.ExecuteWithRetryAsync(
            $"IoTHubUpload_{Path.GetFileName(filePath)}",
            async (ct) =>
            {
              // ストリームの位置をリセット (リトライ時に必要)
              if (fileStream.CanSeek)
              {
                fileStream.Seek(0, SeekOrigin.Begin);
              }
              else
              {
                // シークできないストリームの場合は警告をログに記録
                _logger.LogWarning("ストリームをシークできません。リトライ時に問題が発生する可能性があります: {FilePath}", filePath);
              }
              await UploadFileToIoTHubAsync(fileStream, blobName, ct).ConfigureAwait(false);
              _logger.LogDebug("ファイルのアップロードが成功しました: {FilePath} -> {BlobName}", filePath, blobName);
            },
            MachineLog.Common.Utilities.RetryPolicy.CreateIoTHubRetryPolicy(_logger, "IoTHubUpload", DefaultRetryCount, DefaultInitialBackoffSeconds),
            context,
            cancellationToken).ConfigureAwait(false);

        result.Success = true;
      }
    }
    catch (Exception ex)
    {
      result.Success = false;
      result.ErrorMessage = ex.Message;
      result.Exception = ex;
      throw; // 上位呼び出し元で処理するために再スロー
    }
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
    // SDK内部のリトライポリシーはシンプルに設定 (主要なリトライはRetryHandlerで行う)
    deviceClient.SetRetryPolicy(new ExponentialBackoff(DefaultRetryCount, TimeSpan.FromSeconds(DefaultInitialBackoffSeconds), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1)));
    deviceClient.OperationTimeoutInMilliseconds = (uint)TimeSpan.FromMinutes(DefaultIoTHubOperationTimeoutMinutes).TotalMilliseconds;

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

    try
    {
      // ステップ1: SASアップロードURIを取得
      var sasUriResponse = await _deviceClient.GetFileUploadSasUriAsync(
          new FileUploadSasUriRequest
          {
            BlobName = uploadPath
          },
          cancellationToken).ConfigureAwait(false);

      _logger.LogDebug("ファイルアップロード用のSAS URIを取得しました: {CorrelationId}", sasUriResponse.CorrelationId);

      // ステップ2: Azure Blob Storageにファイルをアップロードするためのクライアントライブラリを使用
      var blobClient = new Azure.Storage.Blobs.BlobClient(sasUriResponse.GetBlobUri());

      // コンテンツタイプの推定
      string contentType = Path.GetExtension(blobName).ToLowerInvariant() switch
      {
        ".json" or ".jsonl" => JsonContentType,
        ".log" or ".txt" => PlainTextContentType,
        _ => DefaultContentType
      };

      // Azure Blob Storageにアップロード
      var blobUploadOptions = new BlobUploadOptions
      {
        HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
      };

      await blobClient.UploadAsync(fileStream, blobUploadOptions, cancellationToken).ConfigureAwait(false);
      _logger.LogDebug("Blobストレージへのアップロードが完了しました: {CorrelationId}", sasUriResponse.CorrelationId);

      // ステップ3: IoT Hubにアップロード完了を通知
      await _deviceClient.CompleteFileUploadAsync(
          new FileUploadCompletionNotification
          {
            CorrelationId = sasUriResponse.CorrelationId,
            IsSuccess = true
          },
          cancellationToken).ConfigureAwait(false);

      _logger.LogInformation("ファイルのアップロードが完了しました: {Path}", uploadPath);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ファイルアップロード中にエラーが発生しました: {Path}", uploadPath);
      throw; // 上位呼び出し元でリトライするために例外を再スロー
    }
  }

  /// <summary>
  /// 接続状態変更ハンドラ
  /// </summary>
  /// <param name="status">接続状態</param>
  /// <param name="reason">変更理由</param>
  private void ConnectionStatusChangesHandler(ConnectionStatus status, ConnectionStatusChangeReason reason)
  {
    _logger.LogInformation("IoT Hub接続状態が変更されました: {Status}, 理由: {Reason}", status, reason);

    // すでに内部状態がErrorの場合に、一時的な接続回復を検出したら内部状態を更新
    bool wasInErrorState = _connectionState == ConnectionState.Error;

    // 状態の更新
    UpdateConnectionState(status);

    // 再接続が必要かどうかを判断
    bool needsReconnection = ShouldAttemptReconnection(status, reason, wasInErrorState);

    if (needsReconnection)
    {
      _logger.LogWarning("接続状態の変更により再接続が必要です。再接続を試みます: {Status}, {Reason}", status, reason);
      _ = HandleReconnectionAsync();
    }
  }

  /// <summary>
  /// 接続状態を更新します
  /// </summary>
  private void UpdateConnectionState(ConnectionStatus status)
  {
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
  }

  /// <summary>
  /// 再接続を試みるべきかどうかを判断します
  /// </summary>
  private bool ShouldAttemptReconnection(ConnectionStatus status, ConnectionStatusChangeReason reason, bool wasInErrorState)
  {
    // 一時的なエラーで切断された場合
    if (status == ConnectionStatus.Disconnected &&
        (reason == ConnectionStatusChangeReason.Communication_Error ||
         reason == ConnectionStatusChangeReason.Connection_Ok))
    {
      return true;
    }

    // エラー状態から接続状態に回復した場合（完全な再接続が必要）
    if (wasInErrorState && status == ConnectionStatus.Connected)
    {
      return true;
    }

    // クライアントが無効化された場合
    if (status == ConnectionStatus.Disabled)
    {
      _logger.LogWarning("IoT Hubクライアントが無効化されました。自動再接続は行いません。");
      return false;
    }

    return false;
  }

  /// <summary>
  /// 再接続処理を行います
  /// </summary>
  /// <returns>タスク</returns>
  private async Task HandleReconnectionAsync()
  {
    try
    {
      _logger.LogInformation("IoT Hubへの再接続を開始します: {DeviceId}", _config.DeviceId);

      // すでに新しい接続プロセスが進行中の場合は早期リターン
      if (_connectionState == ConnectionState.Connecting)
      {
        _logger.LogWarning("既に接続プロセスが進行中です: {DeviceId}", _config.DeviceId);
        return;
      }

      // ロックを取得（タイムアウト付き）
      var lockTaken = await _connectionLock.WaitAsync(TimeSpan.FromSeconds(ReconnectTimeoutSeconds)).ConfigureAwait(false);
      if (!lockTaken)
      {
        _logger.LogError("再接続のためのロック取得がタイムアウトしました: {DeviceId}", _config.DeviceId);
        return;
      }

      try
      {
        // 既に接続済みの場合は何もしない
        if (_connectionState == ConnectionState.Connected && _deviceClient != null)
        {
          _logger.LogInformation("既にIoT Hubに接続されています。再接続は不要です: {DeviceId}", _config.DeviceId);
          return;
        }

        // 切断が必要なら先に切断
        if (_deviceClient != null)
        {
          _logger.LogInformation("再接続のために既存の接続を閉じます: {DeviceId}", _config.DeviceId);
          try
          {
            // 短いタイムアウトで切断を試みる
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DisconnectTimeoutSeconds));
            await _deviceClient.CloseAsync(cts.Token).ConfigureAwait(false);
          }
          catch (Exception ex)
          {
            // 切断に失敗した場合は警告だけ記録し、リソースを破棄
            _logger.LogWarning(ex, "切断中にエラーが発生しましたが、リソースは破棄します: {DeviceId}", _config.DeviceId);
          }

          // デバイスクライアントを安全に解放
          ResourceUtility.SafeDispose(_logger, _deviceClient, "IoT Hubクライアント (再接続)");
          _deviceClient = null;
        }

        // 再接続
        _connectionState = ConnectionState.Connecting;
        _logger.LogInformation("新しいDeviceClientを作成します: {DeviceId}", _config.DeviceId);

        // キャンセルトークンを作成（タイムアウト付き）
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(ReconnectTimeoutSeconds));

        // DeviceClientを再作成
        _deviceClient = await CreateDeviceClientAsync(cancellationTokenSource.Token).ConfigureAwait(false);

        // 接続状態変更ハンドラを設定
        _deviceClient.SetConnectionStatusChangesHandler(ConnectionStatusChangesHandler);

        // 接続を開く（RetryHandlerを使用）
        await OpenConnectionWithRetryAsync(cancellationTokenSource.Token).ConfigureAwait(false);

        _connectionState = ConnectionState.Connected;
        _logger.LogInformation("IoT Hubへの再接続が完了しました: {DeviceId}", _config.DeviceId);
      }
      finally
      {
        // ロックを確実に解放
        if (_connectionLock.CurrentCount == 0)
        {
          _connectionLock.Release();
        }
      }
    }
    catch (Exception ex)
    {
      _connectionState = ConnectionState.Error;
      _logger.LogError(ex, "IoT Hubへの再接続中にエラーが発生しました: {DeviceId}", _config.DeviceId);
    }
  }
}
