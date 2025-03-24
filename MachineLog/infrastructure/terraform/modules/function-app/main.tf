resource "azurerm_service_plan" "this" {
  count               = var.use_consumption_plan ? 0 : 1
  name                = "asp-func-${var.application_name}-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  os_type             = "Windows"
  sku_name            = var.function_app_plan_sku
  tags                = var.tags
}

resource "azurerm_windows_function_app" "this" {
  name                       = "func-${var.application_name}-${var.environment}"
  location                   = var.location
  resource_group_name        = var.resource_group_name
  storage_account_name       = var.storage_account_name
  storage_account_access_key = var.storage_account_access_key
  service_plan_id            = var.use_consumption_plan ? null : (var.app_service_plan_id != null ? var.app_service_plan_id : azurerm_service_plan.this[0].id)

  site_config {
    always_on = var.use_consumption_plan ? false : true
    application_stack {
      dotnet_version              = "v8.0"
      use_dotnet_isolated_runtime = true
    }
    application_insights_connection_string = var.application_insights_connection_string
    application_insights_key               = var.application_insights_key
  }

  app_settings = {
    "FUNCTIONS_WORKER_RUNTIME"   = "dotnet-isolated"
    "WEBSITE_RUN_FROM_PACKAGE"   = "1"
    "CosmosDB__ConnectionString" = var.cosmos_db_connection_string
    "IoTHub__ConnectionString"   = var.iot_hub_connection_string
    "LogAnalytics__WorkspaceId"  = var.log_analytics_workspace_id
    "LogAnalytics__ApiVersion"   = "2022-10-01"
    "AzureAd__Instance"          = "https://login.microsoftonline.com/"
    "AzureAd__TenantId"          = var.tenant_id
    "AzureAd__ClientId"          = var.client_id
  }

  identity {
    type = "SystemAssigned"
  }

  tags = var.tags
}

resource "azurerm_app_service_virtual_network_swift_connection" "this" {
  app_service_id = azurerm_windows_function_app.this.id
  subnet_id      = var.subnet_id
}

# Function App診断設定
resource "azurerm_monitor_diagnostic_setting" "function_app" {
  name                       = "diag-${azurerm_windows_function_app.this.name}"
  target_resource_id         = azurerm_windows_function_app.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_resource_id

  enabled_log {
    category_group = "allLogs"
  }

  metric {
    category = "AllMetrics"
    enabled  = true
  }
}
