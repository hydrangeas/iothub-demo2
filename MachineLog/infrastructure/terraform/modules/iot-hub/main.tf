resource "azurerm_iothub" "this" {
  name                = "iot-${var.application_name}-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location

  sku {
    name     = var.iot_hub_sku
    capacity = var.iot_hub_capacity
  }

  file_upload {
    connection_string  = var.storage_connection_string
    container_name     = "logs"
    sas_ttl            = "PT1H" # 1時間
    notifications      = true
    lock_duration      = "PT1M" # 1分
    default_ttl        = "PT1D" # 1日
    max_delivery_count = 10
  }

  # Azure IoT Hub の最新バージョンでは ip_filter_rule が削除されました
  # 代わりに network_rule_set を使用します
  network_rule_set {
    default_action = "Allow"
  }

  enrichment {
    key            = "deviceType"
    value          = "$twin.tags.deviceType"
    endpoint_names = ["events"]
  }

  enrichment {
    key            = "location"
    value          = "$twin.tags.location"
    endpoint_names = ["events"]
  }

  # ストレージへのルーティングエンドポイント
  route {
    name           = "LogsToStorage"
    source         = "DeviceMessages"
    condition      = "true"
    endpoint_names = ["storage"]
    enabled        = true
  }

  endpoint {
    type                       = "AzureIotHub.StorageContainer"
    name                       = "storage"
    connection_string          = var.storage_connection_string
    container_name             = "logs"
    file_name_format           = "{iothub}/{partition}/{YYYY}/{MM}/{DD}/{HH}/{mm}"
    batch_frequency_in_seconds = 60
    max_chunk_size_in_bytes    = 10485760
    encoding                   = "JSON"
  }

  tags = var.tags
}

# IoTHub診断設定
resource "azurerm_monitor_diagnostic_setting" "iothub" {
  name                       = "diag-${azurerm_iothub.this.name}"
  target_resource_id         = azurerm_iothub.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }

  metric {
    category = "AllMetrics"
    enabled  = true
  }
}

# IoT Hub Consumer Group
resource "azurerm_iothub_consumer_group" "function" {
  name                   = "function"
  iothub_name            = azurerm_iothub.this.name
  eventhub_endpoint_name = "events"
  resource_group_name    = var.resource_group_name
}

resource "azurerm_iothub_consumer_group" "stream_analytics" {
  name                   = "streamanalytics"
  iothub_name            = azurerm_iothub.this.name
  eventhub_endpoint_name = "events"
  resource_group_name    = var.resource_group_name
}

# IoT Hub Shared Access Policy
resource "azurerm_iothub_shared_access_policy" "service" {
  name                = "service"
  iothub_name         = azurerm_iothub.this.name
  resource_group_name = var.resource_group_name

  registry_read   = true
  registry_write  = true
  service_connect = true
}

resource "azurerm_iothub_shared_access_policy" "device" {
  name                = "device"
  iothub_name         = azurerm_iothub.this.name
  resource_group_name = var.resource_group_name

  device_connect = true
}
