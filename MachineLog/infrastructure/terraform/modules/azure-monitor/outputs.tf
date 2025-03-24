output "log_analytics_workspace_id" {
  description = "Log Analyticsワークスペースの一意識別子"
  value       = azurerm_log_analytics_workspace.this.workspace_id
}

output "log_analytics_workspace_resource_id" {
  description = "Log AnalyticsワークスペースのAzureリソースID"
  value       = azurerm_log_analytics_workspace.this.id
}

output "application_insights_id" {
  description = "Application InsightsのID"
  value       = azurerm_application_insights.this.id
}

output "application_insights_app_id" {
  description = "Application InsightsのアプリケーションID"
  value       = azurerm_application_insights.this.app_id
}

output "application_insights_instrumentation_key" {
  description = "Application Insightsのインストルメンテーションキー"
  value       = azurerm_application_insights.this.instrumentation_key
  sensitive   = true
}

output "application_insights_connection_string" {
  description = "Application Insightsの接続文字列"
  value       = azurerm_application_insights.this.connection_string
  sensitive   = true
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

output "action_group_id" {
  description = "アクショングループのID"
  value       = azurerm_monitor_action_group.error_alert.id
}
