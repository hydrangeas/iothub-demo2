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
  backend "azurerm" {
    resource_group_name  = "rg-terraform-state"
    storage_account_name = "stterraformstate"
    container_name       = "machinelog"
    key                  = "test.terraform.tfstate"
  }
}

provider "azurerm" {
  features {}
}

provider "azuread" {}

data "azurerm_client_config" "current" {}

locals {
  environment = "test"
  location    = "japaneast"
  tags = {
    Environment = "Test"
    Project     = "MachineLog"
    Owner       = "TestTeam"
    ManagedBy   = "Terraform"
  }
}

resource "azurerm_resource_group" "this" {
  name     = "rg-machinelog-${local.environment}"
  location = local.location
  tags     = local.tags
}

module "azure_monitor" {
  source = "../../modules/azure-monitor"

  resource_group_name   = azurerm_resource_group.this.name
  location              = local.location
  environment           = local.environment
  log_analytics_sku     = "PerGB2018"
  log_retention_in_days = 90
  tags                  = local.tags
}

module "azure_storage" {
  source = "../../modules/azure-storage"

  resource_group_name      = azurerm_resource_group.this.name
  location                 = local.location
  environment              = local.environment
  storage_account_tier     = "Standard"
  storage_replication_type = "GRS"
  log_retention_in_days    = 90
  tags                     = local.tags
}

module "entra_id" {
  source = "../../modules/entra-id"

  application_name     = "MachineLog"
  environment          = local.environment
  homepage_url         = "https://app-machinelog-${local.environment}.azurewebsites.net"
  redirect_uris        = ["https://app-machinelog-${local.environment}.azurewebsites.net/signin-oidc"]
  create_client_secret = true
}

module "app_service" {
  source = "../../modules/app-service"

  resource_group_name                 = azurerm_resource_group.this.name
  location                            = local.location
  environment                         = local.environment
  app_service_plan_sku                = "S1"
  aspnet_environment                  = "Staging"
  log_analytics_workspace_id          = module.azure_monitor.log_analytics_workspace_id
  log_analytics_workspace_resource_id = module.azure_monitor.log_analytics_workspace_resource_id
  tenant_id                           = data.azurerm_client_config.current.tenant_id
  client_id                           = module.entra_id.client_id
  tags                                = local.tags
}
