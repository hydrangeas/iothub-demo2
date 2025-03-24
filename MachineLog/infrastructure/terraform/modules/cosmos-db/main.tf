resource "azurerm_cosmosdb_account" "this" {
  name                = "cosmos-${var.application_name}-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  offer_type          = var.cosmos_db_offer_type
  kind                = "GlobalDocumentDB"

  consistency_policy {
    consistency_level       = var.cosmos_db_consistency
    max_interval_in_seconds = 300
    max_staleness_prefix    = 100000
  }

  geo_location {
    location          = var.location
    failover_priority = 0
  }

  dynamic "geo_location" {
    for_each = var.cosmos_db_failover_location != null ? [1] : []
    content {
      location          = var.cosmos_db_failover_location
      failover_priority = 1
    }
  }

  capabilities {
    name = "EnableServerless"
  }

  capabilities {
    name = "EnableAggregationPipeline"
  }

  is_virtual_network_filter_enabled = var.allowed_subnet_ids != null && length(var.allowed_subnet_ids) > 0 ? true : false

  dynamic "virtual_network_rule" {
    for_each = var.allowed_subnet_ids != null ? var.allowed_subnet_ids : []
    content {
      id = virtual_network_rule.value
    }
  }

  tags = var.tags
}

resource "azurerm_cosmosdb_sql_database" "this" {
  name                = "MachineLogDB"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
}

resource "azurerm_cosmosdb_sql_container" "logEntries" {
  name                = "logEntries"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/machineId"]

  indexing_policy {
    indexing_mode = "consistent"

    included_path {
      path = "/*"
    }

    excluded_path {
      path = "/\"_etag\"/?"
    }
  }

  # 14日間のTTL設定（秒単位）
  default_ttl = 1209600
}

# Cosmos DB診断設定
resource "azurerm_monitor_diagnostic_setting" "cosmos" {
  name                       = "diag-${azurerm_cosmosdb_account.this.name}"
  target_resource_id         = azurerm_cosmosdb_account.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }

  metric {
    category = "AllMetrics"
    enabled  = true
  }
}
