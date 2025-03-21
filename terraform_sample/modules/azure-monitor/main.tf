resource "azurerm_resource_group" "this" {
  count    = var.create_resource_group ? 1 : 0
  name     = var.resource_group_name
  location = var.location
  tags     = var.tags
}

resource "azurerm_log_analytics_workspace" "this" {
  name                = "law-machinelog-${var.environment}"
  location            = var.location
  resource_group_name = var.create_resource_group ? azurerm_resource_group.this[0].name : var.resource_group_name
  sku                 = var.log_analytics_sku
  retention_in_days   = var.log_retention_in_days
  tags                = var.tags
}

resource "azurerm_monitor_data_collection_endpoint" "this" {
  name                = "dce-machinelog-${var.environment}"
  location            = var.location
  resource_group_name = var.create_resource_group ? azurerm_resource_group.this[0].name : var.resource_group_name
  kind                = "Linux"
  tags                = var.tags
}

resource "azurerm_monitor_data_collection_rule" "this" {
  name                = "dcr-machinelog-${var.environment}"
  location            = var.location
  resource_group_name = var.create_resource_group ? azurerm_resource_group.this[0].name : var.resource_group_name
  kind                = "Linux"

  destinations {
    log_analytics {
      workspace_resource_id = azurerm_log_analytics_workspace.this.id
      name                  = "log-analytics-destination"
    }
  }

  data_flow {
    streams      = ["Microsoft-Syslog"]
    destinations = ["log-analytics-destination"]
  }

  data_sources {
    syslog {
      name           = "syslog-source"
      facility_names = ["*"]
      log_levels     = ["*"]
      streams        = ["Microsoft-Syslog"]
    }
  }

  tags = var.tags
}

# 保存クエリの作成
resource "azurerm_log_analytics_saved_search" "error_logs" {
  name                       = "ErrorLogs"
  category                   = "MachineLog"
  display_name               = "エラーログ"
  query                      = "MachineLog_CL | where Severity == 'Error' | order by TimeGenerated desc"
  function_alias             = "ErrorLogs"
  function_parameters        = []
  log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
}

resource "azurerm_log_analytics_saved_search" "machine_status" {
  name                       = "MachineStatus"
  category                   = "MachineLog"
  display_name               = "機械ステータス"
  query                      = "MachineLog_CL | summarize LastLog=max(TimeGenerated) by MachineId_s | extend Status = iff(LastLog < ago(1h), 'Offline', 'Online')"
  function_alias             = "MachineStatus"
  function_parameters        = []
  log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
}

# アラートルールの作成
# アクショングループの作成
resource "azurerm_monitor_action_group" "error_alert" {
  name                = "ag-error-alert-${var.environment}"
  resource_group_name = var.create_resource_group ? azurerm_resource_group.this[0].name : var.resource_group_name
  short_name          = "ErrorAlert"

  email_receiver {
    name                    = "admin"
    email_address           = var.alert_email_address
    use_common_alert_schema = true
  }
}

resource "azurerm_monitor_scheduled_query_rules_alert" "error_alert" {
  name                = "alert-error-logs-${var.environment}"
  location            = var.location
  resource_group_name = var.create_resource_group ? azurerm_resource_group.this[0].name : var.resource_group_name

  action {
    action_group           = [azurerm_monitor_action_group.error_alert.id]
    email_subject          = "MachineLog Error Alert"
    custom_webhook_payload = "{}"
  }

  data_source_id = azurerm_log_analytics_workspace.this.id
  description    = "エラーログが検出されたときのアラート"
  enabled        = true

  query       = "MachineLog_CL | where Severity == 'Error'"
  severity    = 1
  frequency   = 5
  time_window = 5

  trigger {
    operator  = "GreaterThan"
    threshold = 0
  }

  tags = var.tags
}
