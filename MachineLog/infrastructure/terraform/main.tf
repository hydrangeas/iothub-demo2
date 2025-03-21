# MachineLog インフラストラクチャ定義
# メインTerraformファイル

terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }
  backend "azurerm" {
    # バックエンド設定は環境ごとに異なるため、backend.conf ファイルで指定
  }
}

provider "azurerm" {
  features {}
}

# 共通変数
variable "project" {
  description = "プロジェクト名"
  type        = string
  default     = "machinelog"
}

variable "environment" {
  description = "環境名 (dev, test, prod)"
  type        = string
}

variable "location" {
  description = "Azureリージョン"
  type        = string
  default     = "japaneast"
}

# リソースグループ
resource "azurerm_resource_group" "main" {
  name     = "rg-${var.project}-${var.environment}"
  location = var.location
  tags = {
    Environment = var.environment
    Project     = var.project
  }
}

# 各モジュールの呼び出し
# 実際の実装は各モジュールディレクトリに定義

module "iot_hub" {
  source = "./modules/iot-hub"

  resource_group_name = azurerm_resource_group.main.name
  location            = var.location
  environment         = var.environment
  project             = var.project
}

module "storage" {
  source = "./modules/storage"

  resource_group_name = azurerm_resource_group.main.name
  location            = var.location
  environment         = var.environment
  project             = var.project
}

module "app_service" {
  source = "./modules/app-service"

  resource_group_name = azurerm_resource_group.main.name
  location            = var.location
  environment         = var.environment
  project             = var.project
}

module "key_vault" {
  source = "./modules/key-vault"

  resource_group_name = azurerm_resource_group.main.name
  location            = var.location
  environment         = var.environment
  project             = var.project
}

module "monitoring" {
  source = "./modules/monitoring"

  resource_group_name = azurerm_resource_group.main.name
  location            = var.location
  environment         = var.environment
  project             = var.project
}

# 出力値
output "resource_group_name" {
  value = azurerm_resource_group.main.name
}

output "iot_hub_name" {
  value = module.iot_hub.name
}

output "storage_account_name" {
  value = module.storage.account_name
}

output "app_service_url" {
  value = module.app_service.url
}
