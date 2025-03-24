output "app_service_id" {
  description = "App ServiceのID"
  value       = azurerm_windows_web_app.this.id
}

output "app_service_name" {
  description = "App Serviceの名前"
  value       = azurerm_windows_web_app.this.name
}

output "app_service_default_hostname" {
  description = "App Serviceのデフォルトホスト名"
  value       = azurerm_windows_web_app.this.default_hostname
}

output "app_service_principal_id" {
  description = "App ServiceのマネージドアイデンティティのプリンシパルID"
  value       = azurerm_windows_web_app.this.identity[0].principal_id
}

output "app_service_plan_id" {
  description = "App ServiceプランのID"
  value       = azurerm_service_plan.this.id
}

output "app_service_plan_name" {
  description = "App Serviceプランの名前"
  value       = azurerm_service_plan.this.name
}

output "app_service_outbound_ip_addresses" {
  description = "App Serviceの送信IPアドレス"
  value       = azurerm_windows_web_app.this.outbound_ip_addresses
}
