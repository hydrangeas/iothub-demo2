output "log_analytics_workspace_id" {
  description = "Log Analyticsワークスペースの一意識別子"
  value       = azurerm_log_analytics_workspace.this.workspace_id
}

output "log_analytics_workspace_resource_id" {
  description = "Log AnalyticsワークスペースのAzureリソースID"
  value       = azurerm_log_analytics_workspace.this.id
}

output "data_collection_endpoint_id" {
  description = "データ収集エンドポイントのID"
  value       = azurerm_monitor_data_collection_endpoint.this.id
}

output "data_collection_rule_id" {
  description = "データ収集ルールのID"
  value       = azurerm_monitor_data_collection_rule.this.id
}

output "data_collection_endpoint_url" {
  description = "データ収集エンドポイントのURL"
  value       = azurerm_monitor_data_collection_endpoint.this.logs_ingestion_endpoint
}
