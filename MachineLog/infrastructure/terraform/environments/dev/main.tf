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
}

provider "azuread" {}

data "azurerm_client_config" "current" {}

locals {
  environment = "dev"
  location    = "japaneast"
  tags = {
    Environment = "Development"
    Project     = "MachineLog"
    Owner       = "DevTeam"
    ManagedBy   = "Terraform"
  }
}

module "networking" {
  source = "../../modules/networking"

  resource_group_name = var.resource_group_name
  location            = var.location
  environment         = local.environment
  address_space       = var.address_space
  subnet_prefixes     = var.subnet_prefixes
  subnet_names        = var.subnet_names
  tags                = local.tags
}

module "azure_monitor" {
  source = "../../modules/azure-monitor"

  resource_group_name   = var.resource_group_name
  location              = var.location
  environment           = local.environment
  application_name      = var.application_name
  log_analytics_sku     = var.log_analytics_sku
  log_retention_in_days = var.log_retention_in_days
  alert_email_address   = var.alert_email_address
  tags                  = local.tags
}

module "azure_storage" {
  source = "../../modules/azure-storage"

  resource_group_name                  = var.resource_group_name
  location                             = var.location
  environment                          = local.environment
  application_name                     = var.application_name
  storage_account_tier                 = var.storage_account_tier
  storage_replication_type             = var.storage_replication_type
  blob_soft_delete_retention_days      = var.blob_soft_delete_retention_days
  container_soft_delete_retention_days = var.container_soft_delete_retention_days
  log_retention_in_days                = var.log_retention_in_days
  enable_network_rules                 = false
  allowed_subnet_ids                   = [module.networking.app_subnet_id, module.networking.function_subnet_id]
  log_analytics_workspace_id           = module.azure_monitor.log_analytics_workspace_resource_id
  enable_hierarchical_namespace        = true
  tags                                 = local.tags
}

module "iot_hub" {
  source = "../../modules/iot-hub"

  resource_group_name        = var.resource_group_name
  location                   = var.location
  environment                = local.environment
  application_name           = var.application_name
  iot_hub_sku                = var.iot_hub_sku
  iot_hub_capacity           = var.iot_hub_capacity
  storage_connection_string  = module.azure_storage.primary_connection_string
  storage_container_id       = module.azure_storage.logs_container_id
  log_analytics_workspace_id = module.azure_monitor.log_analytics_workspace_resource_id
  tags                       = local.tags
}

module "cosmos_db" {
  source = "../../modules/cosmos-db"

  resource_group_name         = var.resource_group_name
  location                    = var.location
  environment                 = local.environment
  application_name            = var.application_name
  cosmos_db_offer_type        = var.cosmos_db_offer_type
  cosmos_db_consistency       = var.cosmos_db_consistency
  cosmos_db_failover_location = var.cosmos_db_failover_location
  log_analytics_workspace_id  = module.azure_monitor.log_analytics_workspace_resource_id
  allowed_subnet_ids          = [module.networking.app_subnet_id, module.networking.function_subnet_id]
  tags                        = local.tags
}

module "entra_id" {
  source = "../../modules/entra-id"

  application_name     = var.application_name
  environment          = local.environment
  homepage_url         = "https://app-${var.application_name}-${local.environment}.azurewebsites.net"
  redirect_uris        = ["https://app-${var.application_name}-${local.environment}.azurewebsites.net/signin-oidc"]
  create_client_secret = true
}

module "app_service" {
  source = "../../modules/app-service"

  resource_group_name                    = var.resource_group_name
  location                               = var.location
  environment                            = local.environment
  application_name                       = var.application_name
  app_service_plan_sku                   = var.app_service_plan_sku
  subnet_id                              = module.networking.app_subnet_id
  log_analytics_workspace_id             = module.azure_monitor.log_analytics_workspace_id
  log_analytics_workspace_resource_id    = module.azure_monitor.log_analytics_workspace_resource_id
  application_insights_connection_string = module.azure_monitor.application_insights_connection_string
  tenant_id                              = data.azurerm_client_config.current.tenant_id
  client_id                              = module.entra_id.client_id
  aspnet_environment                     = "Development"
  tags                                   = local.tags
}

module "function_app" {
  source = "../../modules/function-app"

  resource_group_name                    = var.resource_group_name
  location                               = var.location
  environment                            = local.environment
  application_name                       = var.application_name
  storage_account_name                   = module.azure_storage.function_storage_account_name
  storage_account_access_key             = module.azure_storage.function_storage_account_key
  app_service_plan_id                    = var.use_consumption_plan ? null : module.app_service.app_service_plan_id
  subnet_id                              = module.networking.function_subnet_id
  cosmos_db_connection_string            = module.cosmos_db.primary_connection_string
  iot_hub_connection_string              = module.iot_hub.connection_string
  log_analytics_workspace_id             = module.azure_monitor.log_analytics_workspace_id
  log_analytics_workspace_resource_id    = module.azure_monitor.log_analytics_workspace_resource_id
  application_insights_connection_string = module.azure_monitor.application_insights_connection_string
  tenant_id                              = data.azurerm_client_config.current.tenant_id
  client_id                              = module.entra_id.client_id
  use_consumption_plan                   = var.use_consumption_plan
  tags                                   = local.tags
}
