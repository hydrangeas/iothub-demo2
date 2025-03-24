output "function_app_id" {
  description = "Function AppのID"
  value       = azurerm_windows_function_app.this.id
}

output "function_app_name" {
  description = "Function Appの名前"
  value       = azurerm_windows_function_app.this.name
}

output "function_app_default_hostname" {
  description = "Function Appのデフォルトホスト名"
  value       = azurerm_windows_function_app.this.default_hostname
}

output "function_app_principal_id" {
  description = "Function AppのマネージドアイデンティティのプリンシパルID"
  value       = azurerm_windows_function_app.this.identity[0].principal_id
}

output "function_app_outbound_ip_addresses" {
  description = "Function Appの送信IPアドレス"
  value       = azurerm_windows_function_app.this.outbound_ip_addresses
}

output "function_app_possible_outbound_ip_addresses" {
  description = "Function Appの可能性のある送信IPアドレス"
  value       = azurerm_windows_function_app.this.possible_outbound_ip_addresses
}

output "function_app_service_plan_id" {
  description = "Function App Serviceプランのid"
  value       = var.use_consumption_plan ? null : (var.app_service_plan_id != null ? var.app_service_plan_id : azurerm_service_plan.this[0].id)
}
