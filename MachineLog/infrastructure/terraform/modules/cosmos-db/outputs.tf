output "id" {
  description = "Cosmos DBアカウントのID"
  value       = azurerm_cosmosdb_account.this.id
}

output "name" {
  description = "Cosmos DBアカウントの名前"
  value       = azurerm_cosmosdb_account.this.name
}

output "endpoint" {
  description = "Cosmos DBのエンドポイント"
  value       = azurerm_cosmosdb_account.this.endpoint
}

output "primary_key" {
  description = "Cosmos DBのプライマリキー"
  value       = azurerm_cosmosdb_account.this.primary_key
  sensitive   = true
}

output "primary_connection_string" {
  description = "Cosmos DBのプライマリ接続文字列"
  value       = azurerm_cosmosdb_account.this.primary_sql_connection_string
  sensitive   = true
}

output "secondary_connection_string" {
  description = "Cosmos DBのセカンダリ接続文字列"
  value       = azurerm_cosmosdb_account.this.secondary_sql_connection_string
  sensitive   = true
}

output "database_name" {
  description = "Cosmos DBのデータベース名"
  value       = azurerm_cosmosdb_sql_database.this.name
}

output "logEntries_container_name" {
  description = "ログエントリコンテナの名前"
  value       = azurerm_cosmosdb_sql_container.logEntries.name
}
