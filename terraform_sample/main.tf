terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 2.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.0"
    }
  }
  backend "azurerm" {}
}

provider "azurerm" {
  features {}
  use_cli                    = true
  skip_provider_registration = true

  client_id       = var.client_id != "" ? var.client_id : null
  client_secret   = var.client_secret != "" ? var.client_secret : null
  tenant_id       = var.tenant_id != "" ? var.tenant_id : null
  subscription_id = var.subscription_id != "" ? var.subscription_id : null
}

provider "azuread" {
  use_cli = true

  client_id     = var.client_id != "" ? var.client_id : null
  client_secret = var.client_secret != "" ? var.client_secret : null
  tenant_id     = var.tenant_id != "" ? var.tenant_id : null
}

data "azurerm_client_config" "current" {}

# リソースグループの作成
resource "azurerm_resource_group" "this" {
  name     = var.resource_group_name
  location = var.location
  tags     = var.tags
}

# モジュールの呼び出し
module "azure_monitor" {
  source = "./modules/azure-monitor"

  resource_group_name   = azurerm_resource_group.this.name
  location              = var.location
  environment           = var.environment
  log_analytics_sku     = var.log_analytics_sku
  log_retention_in_days = var.log_retention_in_days
  tags                  = var.tags
}

module "azure_storage" {
  source = "./modules/azure-storage"

  resource_group_name      = azurerm_resource_group.this.name
  location                 = var.location
  environment              = var.environment
  storage_account_tier     = var.storage_account_tier
  storage_replication_type = var.storage_replication_type
  tags                     = var.tags
}

module "app_service" {
  source = "./modules/app-service"

  resource_group_name                 = azurerm_resource_group.this.name
  location                            = var.location
  environment                         = var.environment
  app_service_plan_sku                = var.app_service_plan_sku
  log_analytics_workspace_id          = module.azure_monitor.log_analytics_workspace_id
  log_analytics_workspace_resource_id = module.azure_monitor.log_analytics_workspace_resource_id
  tenant_id                           = data.azurerm_client_config.current.tenant_id
  client_id                           = module.entra_id.client_id
  tags                                = var.tags
}

module "entra_id" {
  source = "./modules/entra-id"

  application_name = "MachineLog"
  environment      = var.environment
}
