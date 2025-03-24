output "resource_group_name" {
  description = "作成されたリソースグループの名前"
  value       = var.resource_group_name
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

# ネットワーク関連の出力
output "virtual_network_id" {
  description = "仮想ネットワークのID"
  value       = module.networking.virtual_network_id
}

output "app_subnet_id" {
  description = "App Service用サブネットのID"
  value       = module.networking.app_subnet_id
}

output "function_subnet_id" {
  description = "Function App用サブネットのID"
  value       = module.networking.function_subnet_id
}

# ストレージ関連の出力
output "storage_account_name" {
  description = "ストレージアカウントの名前"
  value       = module.azure_storage.storage_account_name
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
