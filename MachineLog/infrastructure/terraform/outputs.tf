output "resource_group_name" {
  description = "作成されたリソースグループの名前"
  value       = azurerm_resource_group.this.name
}

output "resource_group_id" {
  description = "作成されたリソースグループのID"
  value       = azurerm_resource_group.this.id
}

# Azure Monitor関連の出力
output "log_analytics_workspace_id" {
  description = "Log Analyticsワークスペースの一意識別子"
  value       = module.azure_monitor.log_analytics_workspace_id
}

output "log_analytics_workspace_resource_id" {
  description = "Log AnalyticsワークスペースのAzureリソースID"
  value       = module.azure_monitor.log_analytics_workspace_resource_id
}

output "application_insights_connection_string" {
  description = "Application Insightsの接続文字列"
  value       = module.azure_monitor.application_insights_connection_string
  sensitive   = true
}

# ストレージ関連の出力
output "storage_account_name" {
  description = "ストレージアカウントの名前"
  value       = module.azure_storage.storage_account_name
}

output "storage_account_id" {
  description = "ストレージアカウントのID"
  value       = module.azure_storage.storage_account_id
}

output "primary_blob_endpoint" {
  description = "プライマリBLOBエンドポイント"
  value       = module.azure_storage.primary_blob_endpoint
}

# IoT Hub関連の出力
output "iot_hub_name" {
  description = "IoT Hubの名前"
  value       = module.iot_hub.name
}

output "iot_hub_hostname" {
  description = "IoT Hubのホスト名"
  value       = module.iot_hub.hostname
}

# Cosmos DB関連の出力
output "cosmos_db_name" {
  description = "Cosmos DBアカウントの名前"
  value       = module.cosmos_db.name
}

output "cosmos_db_endpoint" {
  description = "Cosmos DBのエンドポイント"
  value       = module.cosmos_db.endpoint
}

# App Service関連の出力
output "app_service_name" {
  description = "App Serviceの名前"
  value       = module.app_service.app_service_name
}

output "app_service_default_hostname" {
  description = "App Serviceのデフォルトホスト名"
  value       = module.app_service.app_service_default_hostname
}

# Function App関連の出力
output "function_app_name" {
  description = "Function Appの名前"
  value       = module.function_app.function_app_name
}

output "function_app_default_hostname" {
  description = "Function Appのデフォルトホスト名"
  value       = module.function_app.function_app_default_hostname
}

# Entra ID関連の出力
output "application_id" {
  description = "アプリケーションのID"
  value       = module.entra_id.application_id
}

output "client_id" {
  description = "アプリケーションのクライアントID"
  value       = module.entra_id.client_id
}

# Front Door関連の出力は最新のアーキテクチャ設計から削除されました
