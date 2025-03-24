output "id" {
  description = "IoT HubのID"
  value       = azurerm_iothub.this.id
}

output "name" {
  description = "IoT Hubの名前"
  value       = azurerm_iothub.this.name
}

output "hostname" {
  description = "IoT Hubのホスト名"
  value       = azurerm_iothub.this.hostname
}

output "event_hub_events_endpoint" {
  description = "IoT HubのEvent Hub互換エンドポイント"
  value       = azurerm_iothub.this.event_hub_events_endpoint
}

output "event_hub_events_path" {
  description = "IoT HubのEvent Hub互換パス"
  value       = azurerm_iothub.this.event_hub_events_path
}

output "connection_string" {
  description = "IoT Hubのサービス接続文字列"
  value       = azurerm_iothub_shared_access_policy.service.primary_connection_string
  sensitive   = true
}

output "device_connection_string" {
  description = "IoT Hubのデバイス接続文字列"
  value       = azurerm_iothub_shared_access_policy.device.primary_connection_string
  sensitive   = true
}

output "function_consumer_group_name" {
  description = "Function App用のコンシューマーグループ名"
  value       = azurerm_iothub_consumer_group.function.name
}

output "stream_analytics_consumer_group_name" {
  description = "Stream Analytics用のコンシューマーグループ名"
  value       = azurerm_iothub_consumer_group.stream_analytics.name
}
