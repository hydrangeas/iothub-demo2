output "virtual_network_id" {
  description = "仮想ネットワークのID"
  value       = azurerm_virtual_network.this.id
}

output "virtual_network_name" {
  description = "仮想ネットワークの名前"
  value       = azurerm_virtual_network.this.name
}

output "subnet_ids" {
  description = "サブネットIDのリスト"
  value       = azurerm_subnet.subnets[*].id
}

output "subnet_names" {
  description = "サブネット名のリスト"
  value       = azurerm_subnet.subnets[*].name
}

output "app_subnet_id" {
  description = "App Service用サブネットのID"
  value       = azurerm_subnet.subnets[index(var.subnet_names, "app-subnet")].id
}

output "function_subnet_id" {
  description = "Function App用サブネットのID"
  value       = azurerm_subnet.subnets[index(var.subnet_names, "function-subnet")].id
}

output "db_subnet_id" {
  description = "データベース用サブネットのID"
  value       = azurerm_subnet.subnets[index(var.subnet_names, "db-subnet")].id
}

output "app_nsg_id" {
  description = "App Service用NSGのID"
  value       = azurerm_network_security_group.app.id
}

output "storage_private_dns_zone_id" {
  description = "ストレージ用プライベートDNSゾーンのID"
  value       = azurerm_private_dns_zone.storage.id
}

output "cosmos_private_dns_zone_id" {
  description = "Cosmos DB用プライベートDNSゾーンのID"
  value       = azurerm_private_dns_zone.cosmos.id
}
