resource "azurerm_resource_group" "this" {
  count    = var.create_resource_group ? 1 : 0
  name     = var.resource_group_name
  location = var.location
  tags     = var.tags
}

resource "azurerm_service_plan" "this" {
  name                = "asp-machinelog-${var.environment}"
  location            = var.location
  resource_group_name = var.create_resource_group ? azurerm_resource_group.this[0].name : var.resource_group_name
  os_type             = "Windows"
  sku_name            = var.app_service_plan_sku
  tags                = var.tags
}

resource "azurerm_windows_web_app" "this" {
  name                = "app-machinelog-${var.environment}"
  location            = var.location
  resource_group_name = var.create_resource_group ? azurerm_resource_group.this[0].name : var.resource_group_name
  service_plan_id     = azurerm_service_plan.this.id

  https_only = true

  site_config {
    always_on           = true
    minimum_tls_version = "1.2"
    ftps_state          = "Disabled"
    health_check_path   = "/health"

    application_stack {
      dotnet_version = "v8.0"
    }
  }

  app_settings = {
    "ASPNETCORE_ENVIRONMENT"                = var.aspnet_environment
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = var.application_insights_connection_string
    "LogAnalytics__WorkspaceId"             = var.log_analytics_workspace_id
    "LogAnalytics__ApiVersion"              = "2022-10-01"
    "AzureAd__Instance"                     = "https://login.microsoftonline.com/"
    "AzureAd__TenantId"                     = var.tenant_id
    "AzureAd__ClientId"                     = var.client_id
  }

  identity {
    type = "SystemAssigned"
  }

  logs {
    application_logs {
      file_system_level = "Information"
    }

    http_logs {
      file_system {
        retention_in_days = 7
        retention_in_mb   = 35
      }
    }
  }

  tags = var.tags
}

resource "azurerm_app_service_virtual_network_swift_connection" "this" {
  count          = var.subnet_id != null ? 1 : 0
  app_service_id = azurerm_windows_web_app.this.id
  subnet_id      = var.subnet_id
}

# Web App診断設定
resource "azurerm_monitor_diagnostic_setting" "web_app" {
  name                       = "diag-${azurerm_windows_web_app.this.name}"
  target_resource_id         = azurerm_windows_web_app.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_resource_id

  enabled_log {
    category_group = "allLogs"
  }

  metric {
    category = "AllMetrics"
    enabled  = true
  }
}
