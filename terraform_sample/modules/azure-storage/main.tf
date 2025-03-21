resource "azurerm_resource_group" "this" {
  count    = var.create_resource_group ? 1 : 0
  name     = var.resource_group_name
  location = var.location
  tags     = var.tags
}

resource "random_string" "storage_account_suffix" {
  length  = 8
  special = false
  upper   = false
}

resource "azurerm_storage_account" "this" {
  name                     = "stmachinelog${var.environment}${random_string.storage_account_suffix.result}"
  resource_group_name      = var.create_resource_group ? azurerm_resource_group.this[0].name : var.resource_group_name
  location                 = var.location
  account_tier             = var.storage_account_tier
  account_replication_type = var.storage_replication_type
  min_tls_version          = "TLS1_2"

  blob_properties {
    delete_retention_policy {
      days = var.blob_soft_delete_retention_days
    }
    container_delete_retention_policy {
      days = var.container_soft_delete_retention_days
    }
  }

  tags = var.tags
}

resource "azurerm_storage_container" "logs" {
  name                  = "logs"
  storage_account_name  = azurerm_storage_account.this.name
  container_access_type = "private"
}

resource "azurerm_storage_container" "archive" {
  name                  = "archive"
  storage_account_name  = azurerm_storage_account.this.name
  container_access_type = "private"
}

resource "azurerm_storage_management_policy" "this" {
  storage_account_id = azurerm_storage_account.this.id

  rule {
    name    = "logs-lifecycle"
    enabled = true
    filters {
      prefix_match = ["logs/"]
      blob_types   = ["blockBlob"]
    }
    actions {
      base_blob {
        tier_to_cool_after_days_since_modification_greater_than    = 30
        tier_to_archive_after_days_since_modification_greater_than = 90
        delete_after_days_since_modification_greater_than          = var.log_retention_in_days
      }
    }
  }
}

resource "azurerm_storage_account_network_rules" "this" {
  count              = var.enable_network_rules ? 1 : 0
  storage_account_id = azurerm_storage_account.this.id

  default_action             = "Deny"
  ip_rules                   = var.allowed_ip_ranges
  virtual_network_subnet_ids = var.allowed_subnet_ids
  bypass                     = ["AzureServices"]
}
