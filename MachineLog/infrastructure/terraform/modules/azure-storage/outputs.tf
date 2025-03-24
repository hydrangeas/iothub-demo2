output "storage_account_id" {
  description = "ストレージアカウントのID"
  value       = azurerm_storage_account.this.id
}

output "storage_account_name" {
  description = "ストレージアカウントの名前"
  value       = azurerm_storage_account.this.name
}

output "function_storage_account_name" {
  description = "Function App用ストレージアカウントの名前"
  value       = azurerm_storage_account.function.name
}

output "function_storage_account_key" {
  description = "Function App用ストレージアカウントのアクセスキー"
  value       = azurerm_storage_account.function.primary_access_key
  sensitive   = true
}

output "primary_blob_endpoint" {
  description = "プライマリBLOBエンドポイント"
  value       = azurerm_storage_account.this.primary_blob_endpoint
}

output "logs_container_name" {
  description = "ログコンテナの名前"
  value       = azurerm_storage_container.logs.name
}

output "logs_container_id" {
  description = "ログコンテナのID"
  value       = "${azurerm_storage_account.this.id}/blobServices/default/containers/${azurerm_storage_container.logs.name}"
}

output "archive_container_name" {
  description = "アーカイブコンテナの名前"
  value       = azurerm_storage_container.archive.name
}

output "primary_connection_string" {
  description = "プライマリ接続文字列"
  value       = azurerm_storage_account.this.primary_connection_string
  sensitive   = true
}
