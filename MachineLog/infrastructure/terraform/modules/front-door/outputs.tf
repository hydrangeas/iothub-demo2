output "id" {
  description = "Front DoorのID"
  value       = azurerm_frontdoor.this.id
}

output "name" {
  description = "Front Doorの名前"
  value       = azurerm_frontdoor.this.name
}

output "frontend_endpoint" {
  description = "デフォルトのフロントエンドエンドポイント"
  value       = "https://${azurerm_frontdoor.this.frontend_endpoint[0].host_name}"
}

output "custom_frontend_endpoint" {
  description = "カスタムドメインのフロントエンドエンドポイント"
  value       = var.custom_domain != null ? "https://${var.custom_domain}" : null
}

output "waf_policy_id" {
  description = "WAFポリシーのID"
  value       = var.waf_enabled ? azurerm_frontdoor_firewall_policy.this[0].id : null
}

output "waf_policy_name" {
  description = "WAFポリシーの名前"
  value       = var.waf_enabled ? azurerm_frontdoor_firewall_policy.this[0].name : null
}
