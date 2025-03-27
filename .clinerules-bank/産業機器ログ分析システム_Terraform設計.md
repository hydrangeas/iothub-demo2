# 産業機器ログ収集・分析プラットフォーム Terraform設計

## 1. 概要

本文書は、産業機器ログ収集・分析プラットフォーム（MachineLog）のインフラストラクチャをTerraformで実装するための詳細な設計を提供します。この設計は既存の`terraform_sample`を参考にしつつ、システムの要件に合わせて拡張しています。

## 2. 全体構成

インフラストラクチャは以下のモジュールで構成されます：

```text
terraform-machinelog/
├── main.tf                       # メインの設定ファイル
├── variables.tf                  # 変数定義
├── outputs.tf                    # 出力変数
├── terraform.tfvars.example      # 変数値のサンプル
├── backend.conf.example          # バックエンド設定のサンプル
├── environments/                 # 環境別設定
│   ├── dev/                      # 開発環境
│   ├── test/                     # テスト環境
│   └── prod/                     # 本番環境
└── modules/                      # モジュール
    ├── iot-hub/                  # Azure IoT Hubモジュール
    ├── storage/                  # Azure Storageモジュール
    ├── app-service/              # App Serviceモジュール
    ├── function-app/             # Function Appモジュール
    ├── cosmos-db/                # Cosmos DBモジュール
    ├── entra-id/                 # Microsoft Entra ID (Azure AD)モジュール
    ├── monitoring/               # 監視モジュール
    ├── networking/               # ネットワークモジュール
    └── front-door/               # Azure Front Doorモジュール
```

## 3. main.tf

全体の構成を定義する`main.tf`ファイルの内容：

```hcl
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
  name     = "rg-machinelog-${var.environment}"
  location = var.location
  tags     = var.tags
}

# ネットワークモジュールの呼び出し
module "networking" {
  source = "./modules/networking"
  count  = var.deploy_virtual_network ? 1 : 0

  resource_group_name = azurerm_resource_group.this.name
  location            = var.location
  environment         = var.environment
  address_space       = var.address_space
  subnet_prefixes     = var.subnet_prefixes
  subnet_names        = var.subnet_names
  tags                = var.tags
}

# モニタリングモジュールの呼び出し
module "monitoring" {
  source = "./modules/monitoring"

  resource_group_name   = azurerm_resource_group.this.name
  location              = var.location
  environment           = var.environment
  log_analytics_sku     = var.log_analytics_sku
  log_retention_in_days = var.log_retention_in_days
  tags                  = var.tags
}

# ストレージモジュールの呼び出し
module "storage" {
  source = "./modules/storage"

  resource_group_name            = azurerm_resource_group.this.name
  location                       = var.location
  environment                    = var.environment
  storage_account_tier           = var.storage_account_tier
  storage_replication_type       = var.storage_replication_type
  log_retention_in_days          = var.log_retention_in_days
  allowed_subnet_ids             = [module.networking.app_subnet_id, module.networking.function_subnet_id]
  log_analytics_workspace_id     = module.monitoring.log_analytics_workspace_id
  enable_hierarchical_namespace  = true
  enable_network_rules           = var.environment == "prod" ? true : false
  tags                           = var.tags
}
# IoT Hubモジュールの呼び出し
module "iot_hub" {し
  source = "./modules/iot-hub"
  source = "./modules/iot-hub"

  resource_group_name        = azurerm_resource_group.this.name
  location                   = var.location
  environment                = var.environment
  iot_hub_sku                = var.iot_hub_sku
  iot_hub_capacity           = var.iot_hub_capacity
  storage_container_id       = module.storage.logs_container_id
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  tags                       = var.tags
}

# Cosmos DBモジュールの呼び出し
module "cosmos_db" {
  source = "./modules/cosmos-db"

  resource_group_name        = azurerm_resource_group.this.name
  location                   = var.location
  environment                = var.environment
  cosmos_db_offer_type       = var.cosmos_db_offer_type
  cosmos_db_consistency      = var.cosmos_db_consistency
  cosmos_db_failover_location = var.cosmos_db_failover_location
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  allowed_subnet_ids         = [module.networking.app_subnet_id, module.networking.function_subnet_id]
  tags                       = var.tags
}

# Entra IDモジュールの呼び出し
module "entra_id" {
  source = "./modules/entra-id"

  application_name = "MachineLog"
  environment      = var.environment
  api_permissions  = var.api_permissions
}

# Function Appモジュールの呼び出し
module "function_app" {
  source = "./modules/function-app"

  resource_group_name        = azurerm_resource_group.this.name
  location                   = var.location
  environment                = var.environment
  storage_account_name       = module.storage.function_storage_account_name
  storage_account_access_key = module.storage.function_storage_account_key
  app_service_plan_id        = var.use_consumption_plan ? null : module.app_service.app_service_plan_id
  subnet_id                  = module.networking.function_subnet_id
  cosmos_db_connection_string = module.cosmos_db.primary_connection_string
  iot_hub_connection_string  = module.iot_hub.connection_string
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  client_id                  = module.entra_id.client_id
  use_consumption_plan       = var.use_consumption_plan
  tags                       = var.tags
}

# App Serviceモジュールの呼び出し
module "app_service" {
  source = "./modules/app-service"

  resource_group_name        = azurerm_resource_group.this.name
  location                   = var.location
  environment                = var.environment
  app_service_plan_sku       = var.app_service_plan_sku
  subnet_id                  = module.networking.app_subnet_id
  cosmos_db_connection_string = module.cosmos_db.primary_connection_string
  storage_connection_string  = module.storage.primary_connection_string
  application_insights_key   = module.monitoring.application_insights_key
  log_analytics_workspace_id = module.monitoring.log_analytics_workspace_id
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  client_id                  = module.entra_id.client_id
  tags                       = var.tags
}

# Front Doorモジュールの呼び出し（本番環境のみ）
module "front_door" {
  source = "./modules/front-door"
  count  = var.environment == "prod" ? 1 : 0

  resource_group_name = azurerm_resource_group.this.name
  environment         = var.environment
  backend_address     = module.app_service.default_site_hostname
  waf_enabled         = true
  tags                = var.tags
}
```

## 4. variables.tf

プロジェクト全体の変数を定義するファイル：

```hcl
variable "resource_group_name" {
  description = "リソースグループの名前"
  type        = string
  default     = "rg-machinelog"
}

variable "location" {
  description = "リソースのデプロイ先リージョン"
  type        = string
  default     = "japaneast"
}

variable "environment" {
  description = "環境（dev, test, prod）"
  type        = string
  validation {
    condition     = contains(["dev", "test", "prod"], var.environment)
    error_message = "環境は「dev」、「test」、または「prod」のいずれかである必要があります。"
  }
}

variable "tags" {
  description = "リソースに付与するタグ"
  type        = map(string)
  default     = {}
}

# ネットワーク関連設定
variable "address_space" {
  description = "仮想ネットワークのアドレス空間"
  type        = list(string)
  default     = ["10.0.0.0/16"]
}

variable "subnet_prefixes" {
  description = "サブネットのアドレス空間"
  type        = list(string)
  default     = ["10.0.1.0/24", "10.0.2.0/24", "10.0.3.0/24"]
}

variable "subnet_names" {
  description = "サブネット名"
  type        = list(string)
  default     = ["app-subnet", "function-subnet", "db-subnet"]
}

# 監視関連設定
variable "log_analytics_sku" {
  description = "Log Analyticsワークスペースのスキュー"
  type        = string
  default     = "PerGB2018"
}

variable "log_retention_in_days" {
  description = "ログの保持期間（日数）"
  type        = number
  default     = 90
  validation {
    condition     = var.log_retention_in_days >= 30 && var.log_retention_in_days <= 730
    error_message = "ログの保持期間は30日から730日の間である必要があります。"
  }
}

# ストレージ関連設定
variable "storage_account_tier" {
  description = "ストレージアカウントの階層"
  type        = string
  default     = "Standard"
  validation {
    condition     = contains(["Standard", "Premium"], var.storage_account_tier)
    error_message = "ストレージアカウントの階層は「Standard」または「Premium」である必要があります。"
  }
}

variable "storage_replication_type" {
  description = "ストレージアカウントのレプリケーションタイプ"
  type        = string
  default     = "GRS"
  validation {
    condition     = contains(["LRS", "GRS", "RAGRS", "ZRS", "GZRS", "RAGZRS"], var.storage_replication_type)
    error_message = "ストレージアカウントのレプリケーションタイプは有効な値である必要があります。"
  }
}

# IoT Hub関連設定
variable "iot_hub_sku" {
  description = "IoT HubのSKU"
  type        = string
  default     = "S1"  # PoCではF1(無料)を使用
  validation {
    condition     = contains(["F1", "S1", "S2", "S3"], var.iot_hub_sku)
    error_message = "IoT HubのSKUは「F1」、「S1」、「S2」、または「S3」のいずれかである必要があります。"
  }
}

variable "iot_hub_capacity" {
  description = "IoT Hubのユニット数"
  type        = number
  default     = 1  # 最小数の1に設定
  validation {
    condition     = var.iot_hub_capacity >= 1 && var.iot_hub_capacity <= 10
    error_message = "IoT Hubのユニット数は1から10の間である必要があります。"
  }
}

# Cosmos DB関連設定
variable "cosmos_db_offer_type" {
  description = "Cosmos DBのオファータイプ"
  type        = string
  default     = "Standard"
}

variable "cosmos_db_consistency" {
  description = "Cosmos DBの一貫性レベル"
  type        = string
  default     = "Session"
  validation {
    condition     = contains(["Eventual", "Consistent", "Session", "BoundedStaleness", "Strong"], var.cosmos_db_consistency)
    error_message = "Cosmos DBの一貫性レベルは有効な値である必要があります。"
  }
}

variable "cosmos_db_failover_location" {
  description = "Cosmos DBフェイルオーバーのリージョン"
  type        = string
  default     = "japanwest"
}

# App Service関連設定
variable "app_service_plan_sku" {
  description = "App Serviceプランのスキュー"
  type        = string
  default     = "P1v2"
}

# Function App関連設定
variable "use_consumption_plan" {
  description = "Function AppでConsumptionプランを使用するかどうか"
  type        = bool
  default     = false
}

# Microsoft Entra ID（Azure AD）関連設定
variable "api_permissions" {
  description = "アプリケーションに必要なAPI権限"
  type        = list(object({
    api_name = string
    scope    = list(string)
  }))
  default     = [
    {
      api_name = "Microsoft Graph"
      scope    = ["User.Read", "Directory.Read.All"]
    }
  ]
}

# Azure認証関連設定
variable "client_id" {
  description = "Azure サービスプリンシパルのクライアントID"
  type        = string
  default     = ""
}

variable "client_secret" {
  description = "Azure サービスプリンシパルのクライアントシークレット"
  type        = string
  default     = ""
  sensitive   = true
}

variable "tenant_id" {
  description = "Azure テナントID"
  type        = string
  default     = ""
}

variable "subscription_id" {
  description = "Azure サブスクリプションID"
  type        = string
  default     = ""
}

variable "deploy_virtual_network" {
  description = "仮想ネットワークをデプロイするかどうか"
  type        = bool
  default     = true
}
```

## 5. モジュールの詳細設計

### 5.1 IoT Hubモジュール

IoT Hubを管理するモジュールの実装：

```hcl
# modules/iot-hub/main.tf

resource "azurerm_iothub" "this" {
  name                = "iot-machinelog-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  
  sku {
    name     = var.iot_hub_sku
    capacity = var.iot_hub_capacity
  }
  
  file_upload {
    connection_string  = var.storage_connection_string
    container_name     = "logs"
    sas_ttl            = "PT1H"   # 1時間
    notifications      = true
    lock_duration      = "PT1M"   # 1分
    default_ttl        = "PT1D"   # 1日
    max_delivery_count = 10
  }

  ip_filter_rule {
    name    = "AllowAll"
    ip_mask = "0.0.0.0/0"
    action  = "Accept"
  }

  routing_endpoint {
    name               = "StorageEndpoint"
    endpoint_resource_group = var.resource_group_name
    endpoint_subscription_id = data.azurerm_client_config.current.subscription_id
    type               = "AzureStorage"
    resource_id        = var.storage_container_id
    connection_string  = var.storage_connection_string
    routing_rule_name  = "LogsToStorage"
  }

  tags = var.tags
}

# IoTHub診断設定
resource "azurerm_monitor_diagnostic_setting" "iothub" {
  name                       = "diag-${azurerm_iothub.this.name}"
  target_resource_id         = azurerm_iothub.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }

  metric {
    category = "AllMetrics"
    enabled  = true
  }
}
```

### 5.2 Storageモジュール

Blob Storageを管理するモジュールの実装：

```hcl
# modules/storage/main.tf

resource "random_string" "storage_suffix" {
  length  = 8
  special = false
  upper   = false
}

resource "azurerm_storage_account" "main" {
  name                     = "stmachinelog${var.environment}${random_string.storage_suffix.result}"
  resource_group_name      = var.resource_group_name
  location                 = var.location
  account_tier             = var.storage_account_tier
  account_replication_type = var.storage_replication_type
  account_kind             = "StorageV2"
  is_hns_enabled           = var.enable_hierarchical_namespace
  min_tls_version          = "TLS1_2"

  blob_properties {
    delete_retention_policy {
      days = 7
    }
    container_delete_retention_policy {
      days = 7
    }
    versioning_enabled = true
  }

  tags = var.tags
}

resource "azurerm_storage_account" "function" {
  name                     = "stfunc${var.environment}${random_string.storage_suffix.result}"
  resource_group_name      = var.resource_group_name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  account_kind             = "StorageV2"
  min_tls_version          = "TLS1_2"

  tags = var.tags
}

# ログ用コンテナ
resource "azurerm_storage_container" "logs" {
  name                  = "logs"
  storage_account_name  = azurerm_storage_account.main.name
  container_access_type = "private"
}

# アーカイブ用コンテナ
resource "azurerm_storage_container" "archive" {
  name                  = "archive"
  storage_account_name  = azurerm_storage_account.main.name
  container_access_type = "private"
}

# レポート用コンテナ
resource "azurerm_storage_container" "reports" {
  name                  = "reports"
  storage_account_name  = azurerm_storage_account.main.name
  container_access_type = "private"
}

# ライフサイクル管理ポリシー
resource "azurerm_storage_management_policy" "lifecycle" {
  storage_account_id = azurerm_storage_account.main.id

  rule {
    name    = "logs-lifecycle"
    enabled = true
    filters {
      prefix_match = ["logs/"]
      blob_types   = ["blockBlob"]
    }
    actions {
      base_blob {
        tier_to_cool_after_days_since_modification_greater_than    = 90
        tier_to_archive_after_days_since_modification_greater_than = 180
        delete_after_days_since_modification_greater_than          = var.log_retention_in_days
      }
      snapshot {
        delete_after_days_since_creation_greater_than = 30
      }
      version {
        delete_after_days_since_creation_greater_than = 90
      }
    }
  }

  rule {
    name    = "reports-lifecycle"
    enabled = true
    filters {
      prefix_match = ["reports/"]
      blob_types   = ["blockBlob"]
    }
    actions {
      base_blob {
        tier_to_cool_after_days_since_modification_greater_than    = 30
        tier_to_archive_after_days_since_modification_greater_than = 90
        delete_after_days_since_modification_greater_than          = 365
      }
    }
  }
}

# ネットワークルール（オプション）
resource "azurerm_storage_account_network_rules" "network_rules" {
  count              = var.enable_network_rules ? 1 : 0
  storage_account_id = azurerm_storage_account.main.id

  default_action             = "Deny"
  ip_rules                   = var.allowed_ip_ranges
  virtual_network_subnet_ids = var.allowed_subnet_ids
  bypass                     = ["AzureServices"]
}

# 診断設定
resource "azurerm_monitor_diagnostic_setting" "storage" {
  name                       = "diag-${azurerm_storage_account.main.name}"
  target_resource_id         = azurerm_storage_account.main.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  metric {
    category = "Transaction"
    enabled  = true
  }

  metric {
    category = "Capacity"
    enabled  = true
  }
}
```

### 5.3 Cosmos DBモジュール

Cosmos DBを管理するモジュールの実装：

```hcl
# modules/cosmos-db/main.tf

resource "azurerm_cosmosdb_account" "this" {
  name                = "cosmos-machinelog-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  offer_type          = var.cosmos_db_offer_type
  kind                = "GlobalDocumentDB"

  consistency_policy {
    consistency_level = "Session"  # 簡素化した一貫性レベル
  }

  geo_location {
    location          = var.location
    failover_priority = 0
  }

  # PoCでは直近のフェイルオーバー設定を削除

  capabilities {
    name = "EnableServerless"  # PoCではサーバーレスに設定
  }

  capabilities {
    name = "EnableAggregationPipeline"
  }

  # PoCにおける最小リソース設定
  
  tags = var.tags
}

# 必要最小限のコンテナ定義に簡素化
resource "azurerm_cosmosdb_sql_container" "logEntries" {
  name                = "logEntries"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_path  = "/machineId"
  
  indexing_policy {
    indexing_mode = "consistent"
    
    included_path {
      path = "/*"
    }
    
    excluded_path {
      path = "/\"_etag\"/?"
    }
  }

  # PoCでは短い保持期間に設定
  default_ttl = 1209600  # 14日 (60*60*24*14)
}

# 診断設定
resource "azurerm_monitor_diagnostic_setting" "cosmos" {
  name                       = "diag-${azurerm_cosmosdb_account.this.name}"
  target_resource_id         = azurerm_cosmosdb_account.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }

  metric {
    category = "Requests"
    enabled  = true
  }
}
```

### 5.4 Function Appモジュール

Azure Functionsを管理するモジュールの実装：

```hcl
# modules/function-app/main.tf

locals {
  function_app_name = "func-machinelog-${var.environment}"
}

resource "azurerm_service_plan" "consumption" {
  count               = var.use_consumption_plan ? 1 : 0
  name                = "asp-func-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  os_type             = "Windows"
  sku_name            = "Y1"
}

resource "azurerm_windows_function_app" "this" {
  name                       = local.function_app_name
  resource_group_name        = var.resource_group_name
  location                   = var.location
  service_plan_id            = var.use_consumption_plan ? azurerm_service_plan.consumption[0].id : var.app_service_plan_id
  storage_account_name       = var.storage_account_name
  storage_account_access_key = var.storage_account_access_key

  site_config {
    always_on          = !var.use_consumption_plan
    application_stack {
      dotnet_version = "v8.0"
    }
    application_insights_connection_string = var.application_insights_connection_string
    application_insights_key               = var.application_insights_key
  }

    "LogAnalytics__WorkspaceId"        = var.log_analytics_workspace_id
    "AzureAd__TenantId"                = var.tenant_id
    "AzureAd__ClientId"                = var.client_id
  } "FUNCTIONS_WORKER_RUNTIME"         = "dotnet"
    "AzureWebJobsStorage"              = "DefaultEndpointsProtocol=https;AccountName=${var.storage_account_name};AccountKey=${var.storage_account_access_key};EndpointSuffix=core.windows.net"
    "CosmosDB_ConnectionString"        = var.cosmos_db_connection_string
    "IoTHub_ConnectionString"          = var.iot_hub_connection_string
    "LogAnalytics__WorkspaceId"        = var.log_analytics_workspace_id
    "AzureAd__TenantId"                = var.tenant_id
    "AzureAd__ClientId"                = var.client_id
  }

  identity {
    type = "SystemAssigned"
  }

  tags = var.tags
}

resource "azurerm_app_service_virtual_network_swift_connection" "vnet_integration" {
  count          = var.subnet_id != null ? 1 : 0
  app_service_id = azurerm_windows_function_app.this.id
  subnet_id      = var.subnet_id
}

# Function App診断設定
resource "azurerm_monitor_diagnostic_setting" "function_app" {
  name                       = "diag-${local.function_app_name}"
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
```

### 5.5 Networkingモジュール

ネットワーク設定を管理するモジュールの実装：

```hcl
# modules/networking/main.tf

resource "azurerm_virtual_network" "this" {
  name                = "vnet-machinelog-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  address_space       = var.address_space
  tags                = var.tags
}

resource "azurerm_subnet" "app_subnet" {
  name                 = var.subnet_names[0]
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.subnet_prefixes[0]]
  service_endpoints    = ["Microsoft.Storage", "Microsoft.Sql", "Microsoft.KeyVault"]

  delegation {
    name = "delegation"

    service_delegation {
      name    = "Microsoft.Web/serverFarms"
      actions = ["Microsoft.Network/virtualNetworks/subnets/action"]
    }
  }
}

resource "azurerm_subnet" "function_subnet" {
  name                 = var.subnet_names[1]
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.subnet_prefixes[1]]
  service_endpoints    = ["Microsoft.Storage", "Microsoft.Sql", "Microsoft.KeyVault"]

  delegation {
    name = "delegation"

    service_delegation {
      name    = "Microsoft.Web/serverFarms"
      actions = ["Microsoft.Network/virtualNetworks/subnets/action"]
    }
  }
}

resource "azurerm_subnet" "db_subnet" {
  name                 = var.subnet_names[2]
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.subnet_prefixes[2]]
  service_endpoints    = ["Microsoft.Storage", "Microsoft.Sql", "Microsoft.KeyVault", "Microsoft.AzureCosmosDB"]
}

resource "azurerm_network_security_group" "app_nsg" {
  name                = "nsg-app-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags

  security_rule {
    name                       = "Allow-HTTPS-Inbound"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "443"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }
}

resource "azurerm_network_security_group" "function_nsg" {
  name                = "nsg-function-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags

  security_rule {
    name                       = "Allow-HTTPS-Inbound"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "443"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }
}

resource "azurerm_network_security_group" "db_nsg" {
  name                = "nsg-db-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags

  security_rule {
    name                       = "Deny-Internet-Inbound"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Deny"
    protocol                   = "*"
    source_port_range          = "*"
    destination_port_range     = "*"
    source_address_prefix      = "Internet"
    destination_address_prefix = "*"
  }
}

resource "azurerm_subnet_network_security_group_association" "app_nsg_association" {
  subnet_id                 = azurerm_subnet.app_subnet.id
  network_security_group_id = azurerm_network_security_group.app_nsg.id
}

resource "azurerm_subnet_network_security_group_association" "function_nsg_association" {
  subnet_id                 = azurerm_subnet.function_subnet.id
  network_security_group_id = azurerm_network_security_group.function_nsg.id
}

resource "azurerm_subnet_network_security_group_association" "db_nsg_association" {
  subnet_id                 = azurerm_subnet.db_subnet.id
  network_security_group_id = azurerm_network_security_group.db_nsg.id
}
```

### 5.6 Front Doorモジュール

Azure Front Doorを管理するモジュールの実装：

```hcl
# modules/front-door/main.tf

resource "azurerm_frontdoor" "this" {
  name                                         = "fd-machinelog-${var.environment}"
  resource_group_name                          = var.resource_group_name
  enforce_backend_pools_certificate_name_check = true

  routing_rule {
    name               = "defaultRoutingRule"
    accepted_protocols = ["Https"]
    patterns_to_match  = ["/*"]
    frontend_endpoints = ["defaultFrontendEndpoint"]
    forwarding_configuration {
      forwarding_protocol = "HttpsOnly"
      backend_pool_name   = "defaultBackendPool"
    }
  }

  backend_pool_load_balancing {
    name = "defaultLoadBalancing"
  }

  backend_pool_health_probe {
    name      = "defaultHealthProbe"
    protocol  = "Https"
    path      = "/health"
  }

  backend_pool {
    name = "defaultBackendPool"
    backend {
      host_header = var.backend_address
      address     = var.backend_address
      http_port   = 80
      https_port  = 443
      priority    = 1
      weight      = 100
    }

    load_balancing_name = "defaultLoadBalancing"
    health_probe_name   = "defaultHealthProbe"
  }

  frontend_endpoint {
    name                              = "defaultFrontendEndpoint"
    host_name                         = "fd-machinelog-${var.environment}.azurefd.net"
    session_affinity_enabled          = true
    session_affinity_ttl_seconds      = 300
    web_application_firewall_policy_link_id = var.waf_enabled ? azurerm_frontdoor_firewall_policy.waf[0].id : null
  }

  tags = var.tags
}

resource "azurerm_frontdoor_firewall_policy" "waf" {
  count               = var.waf_enabled ? 1 : 0
  name                = "wafpolicy-machinelog-${var.environment}"
  resource_group_name = var.resource_group_name

  enabled                     = true
  mode                        = "Prevention"
  redirect_url                = null
  custom_block_response_body  = null
  custom_block_response_status_code = 403

  managed_rule {
    type    = "DefaultRuleSet"
    version = "1.0"
  }

  managed_rule {
    type    = "Microsoft_BotManagerRuleSet"
    version = "1.0"
  }
}
```

## 6. 環境別設定

各環境（開発、テスト、本番）の特有設定：

### 6.1 開発環境 (dev)

```hcl
# environments/dev/main.tf

terraform {
  backend "azurerm" {}
}

module "machinelog" {
  source = "../../"

  environment                  = "dev"
  resource_group_name          = "rg-machinelog-dev"
  location                     = "japaneast"
  use_consumption_plan         = true   # 開発環境ではFunction AppにConsumptionプランを使用
  # 開発環境固有の設定
  storage_replication_type     = "LRS"  # 開発環境では冗長性を下げてコスト削減
  iot_hub_sku                  = "S1"
  iot_hub_capacity             = 1
  cosmos_db_failover_location  = "japanwest"
  app_service_plan_sku         = "P1v2"
  use_consumption_plan         = true   # 開発環境ではFunction AppにConsumptionプランを使用
  
  log_retention_in_days        = 30     # 開発環境では保持期間を短く

  tags = {
    Environment = "Development"
    Project     = "MachineLog"
    Owner       = "DevTeam"
  }
}
```

### 6.2 テスト環境 (test)

```hcl
# environments/test/main.tf

terraform {
  backend "azurerm" {}
}

module "machinelog" {
  source = "../../"

  environment                  = "test"
  resource_group_name          = "rg-machinelog-test"
  location                     = "japaneast"
  use_consumption_plan         = false  # テスト環境では実際の構成をテスト
  # テスト環境固有の設定
  log_retention_in_days        = 60     # テスト環境では中程度の保持期間
  iot_hub_sku                  = "S1"
  iot_hub_capacity             = 2      # テスト環境では開発より多く
  cosmos_db_failover_location  = null   # テスト環境ではフェイルオーバー設定なし
  app_service_plan_sku         = "P1v2"
  use_consumption_plan         = false  # テスト環境では実際の構成をテスト
  
  log_retention_in_days        = 60     # テスト環境では中程度の保持期間
  
  tags = {
    Environment = "Test"
    Project     = "MachineLog"
    Owner       = "QATeam"
  }
}
```

### 6.3 本番環境 (prod)

```hcl
# environments/prod/main.tf

terraform {
  backend "azurerm" {}
}

module "machinelog" {
  source = "../../"

  environment                  = "prod"
  resource_group_name          = "rg-machinelog-prod"
  location                     = "japaneast"
  use_consumption_plan         = false  # 本番環境ではFunction AppにApp Service Planを使用
  # 本番環境固有の設定
  storage_replication_type     = "GRS"  # 本番環境ではGRSで地理冗長性を確保
  iot_hub_sku                  = "S1"
  iot_hub_capacity             = 4      # 本番環境ではスケールアップ
  cosmos_db_failover_location  = "japanwest"
  app_service_plan_sku         = "P2v2" # 本番環境ではより高性能なPlan
  subnet_names                 = ["app-subnet", "function-subnet", "db-subnet"]用
  
  log_retention_in_days        = 365    # 本番環境では1年間保持
  
    Project     = "MachineLog"
  address_space                = ["10.0.0.0/16"]
  subnet_prefixes              = ["10.0.1.0/24", "10.0.2.0/24", "10.0.3.0/24"]
  subnet_names                 = ["app-subnet", "function-subnet", "db-subnet"]
  
  tags = {
    Environment = "Production"
    Project     = "MachineLog"
    Owner       = "OpsTeam"  
    CostCenter  = "IT-12345"
  }
}
```

### 6.4 PoC環境 (proof of concept)

```hcl
# environments/poc/main.tf

terraform {
  backend "azurerm" {}
}

module "machinelog" {
  source = "../../"

  environment                  = "poc"
  resource_group_name          = "rg-machinelog-poc"
  app_service_plan_sku         = "B1"   # PoCではB1(ベーシック)に縮小
  use_consumption_plan         = true   # サーバーレスで費用対効果を最大化
  # PoCの低コスト設定
  log_retention_in_days        = 14     # PoCでは短期間の保持ル冗長のみ
  iot_hub_sku                  = "S1"   # 最小限のスタンダードプラン
  # ネットワーク構成の最小化             = 1
  cosmos_db_failover_location  = null   # PoCではフェイルオーバー設定を無効化
  app_service_plan_sku         = "B1"   # PoCではB1(ベーシック)に縮小想ネットワークをデプロイしない
  use_consumption_plan         = true   # サーバーレスで費用対効果を最大化
  
  log_retention_in_days        = 14     # PoCでは短期間の保持  Environment = "PoC"
  t     = "MachineLog"
  # 簡素化されたネットワーク構成eam"
  enable_network_rules         = false  # PoCではネットワークルールを簡素化
  address_space                = ["10.0.0.0/16"]
  subnet_prefixes              = ["10.0.1.0/24", "10.0.2.0/24", "10.0.3.0/24"]
  subnet_names                 = ["app-subnet", "function-subnet", "db-subnet"]
  7. CI/CDパイプラインとの統合
  tags = {
    Environment = "PoC"sパイプラインと統合するためのYAMLファイル例：
    Project     = "MachineLog"
    Owner       = "DevTeam"
  }# azure-pipelines.yml
}
```
  branches:
## 7. CI/CDパイプラインとの統合ude:

TerraformをAzure DevOpsパイプラインと統合するためのYAMLファイル例：
e:
```yamlrraform-machinelog/**
# azure-pipelines.yml

trigger:  vmImage: 'ubuntu-latest'
  branches:
    include:
    - main  - group: terraform-secrets
  paths:environment
    include:
    - terraform-machinelog/**rsion

pool:
  vmImage: 'ubuntu-latest'
- stage: Validate
variables:
  - group: terraform-secretsTerraform
  - name: environmentps:
    value: 'dev' # デフォルト環境ller@0
  - name: terraformVersionts:
    value: '1.3.9'aformVersion)

stages:
- stage: Validate  cd terraform-machinelog
  jobs:rm init -backend=false
  - job: ValidateTerraform
    steps:e'
    - task: TerraformInstaller@0
      inputs:
        terraformVersion: $(terraformVersion)inputs:
      
    - script: |ngDirectory: 'terraform-machinelog'
        cd terraform-machinelog: '-check -recursive'
        terraform init -backend=false
        terraform validate
      displayName: 'Terraform Validate'
        dependsOn: Validate
    - task: TerraformCLI@0
      inputs:n
        command: 'fmt'ps:
        workingDirectory: 'terraform-machinelog'nstaller@0
        commandOptions: '-check -recursive'ts:
      displayName: 'Terraform Format Check'aformVersion)

- stage: Plan
  dependsOn: Validate  inputs:
  jobs:
  - job: TerraformPlanngDirectory: 'terraform-machinelog/environments/$(environment)'
    steps:zurerm'
    - task: TerraformInstaller@0
      inputs:GroupName: 'rg-terraform-state'
        terraformVersion: $(terraformVersion)tate$(environment)'
    
    - task: TerraformCLI@0
      inputs:
        command: 'init'
        workingDirectory: 'terraform-machinelog/environments/$(environment)'
        backendType: 'azurerm'inputs:
        backendServiceArm: 'TerraformServiceConnection'
        backendAzureRmResourceGroupName: 'rg-terraform-state'ngDirectory: 'terraform-machinelog/environments/$(environment)'
        backendAzureRmStorageAccountName: 'stterraformstate$(environment)'iceName: 'TerraformServiceConnection'
        backendAzureRmContainerName: 'tfstate'
        backendAzureRmKey: 'machinelog.$(environment).tfstate'
      displayName: 'Terraform Init'
      ingDirectory)/terraform-machinelog/environments/$(environment)/tfplan
    - task: TerraformCLI@0  artifact: tfplan
      inputs:
        command: 'plan'
        workingDirectory: 'terraform-machinelog/environments/$(environment)'
        environmentServiceName: 'TerraformServiceConnection'  dependsOn: Plan
        commandOptions: '-var-file=terraform.tfvars -out=tfplan'ucceeded()
      displayName: 'Terraform Plan'
    rraform
    - publish: $(System.DefaultWorkingDirectory)/terraform-machinelog/environments/$(environment)/tfplanironment: $(environment)
      artifact: tfplan
      displayName: 'Publish Terraform Plan'
y:
- stage: Applys:
  dependsOn: Plank: TerraformInstaller@0
  condition: succeeded()ts:
  jobs:aformVersion)
  - deployment: DeployTerraform
    environment: $(environment)
    strategy:  artifact: tfplan
      runOnce:nload Terraform Plan'
        deploy:
          steps:
          - task: TerraformInstaller@0  inputs:
            inputs:
              terraformVersion: $(terraformVersion)ngDirectory: 'terraform-machinelog/environments/$(environment)'
          zurerm'
          - download: current
            artifact: tfplanGroupName: 'rg-terraform-state'
            displayName: 'Download Terraform Plan'tate$(environment)'
          
          - task: TerraformCLI@0
            inputs:
              command: 'init'
              workingDirectory: 'terraform-machinelog/environments/$(environment)'
              backendType: 'azurerm'  inputs:
              backendServiceArm: 'TerraformServiceConnection'
              backendAzureRmResourceGroupName: 'rg-terraform-state'ngDirectory: 'terraform-machinelog/environments/$(environment)'
              backendAzureRmStorageAccountName: 'stterraformstate$(environment)'ceName: 'TerraformServiceConnection'
              backendAzureRmContainerName: 'tfstate'
              backendAzureRmKey: 'machinelog.$(environment).tfstate'
            displayName: 'Terraform Init'
          
          - task: TerraformCLI@08. セキュリティと運用のベストプラクティス
            inputs:
              command: 'apply'
              workingDirectory: 'terraform-machinelog/environments/$(environment)'
              environmentServiceName: 'TerraformServiceConnection'ットは必ずAzure Key VaultまたはAzure DevOps変数グループで管理
              commandOptions: '$(Pipeline.Workspace)/tfplan'- `terraform.tfvars`ファイルはGitリポジトリには保存せず、`terraform.tfvars.example`をテンプレートとして提供
            displayName: 'Terraform Apply'
```

## 8. セキュリティと運用のベストプラクティス
ファイルは専用のAzure Storage Accountで管理
### 8.1 シークレット管理- 環境ごとに異なるステートファイルを使用し、アクセス制御を適用

- Terraformのシークレットは必ずAzure Key VaultまたはAzure DevOps変数グループで管理
- `terraform.tfvars`ファイルはGitリポジトリには保存せず、`terraform.tfvars.example`をテンプレートとして提供
- バックエンド設定も同様に`backend.conf.example`として提供

### 8.2 ステート管理 Requestを通して承認
- 本番環境の変更は手動承認ステップを含める
- Terraformの状態ファイルは専用のAzure Storage Accountで管理
- 環境ごとに異なるステートファイルを使用し、アクセス制御を適用
- ステート用ストレージアカウントに対する厳格なアクセス制御クフロー）
- 状態ロックの使用によるチーム作業の調整

### 8.3 運用管理
スキャンでインフラストラクチャのドリフトを検出
- 変更はすべてPull Requestを通して承認- 自動または手動の修復プロセスを定義
- 本番環境の変更は手動承認ステップを含めるを早期に検出
- テラフォームプランの詳細なレビュー
- すべての変更を監査ログとして保存
- 計画と適用の分離（GitOpsワークフロー）

### 8.4 ドリフト検出業機器ログ分析システム_実装計画.md」で定義された以下の要件と整合しています：

- 定期的なコンプライアンススキャンでインフラストラクチャのドリフトを検出
- 自動または手動の修復プロセスを定義   - PoC環境用の低コスト設計
- CI/CDの一部として定期的なTerraform計画を実行し、ドリフトを早期に検出段階的展開計画
- インフラ状態の定期監査と報告

## 9. 実装計画との整合性   - 初期段階ではインフラ基盤構築を優先
スからスタートし徐々に拡張
本Terraform設計は「産業機器ログ分析システム_実装計画.md」で定義された以下の要件と整合しています：

1. **フェーズ別デプロイ**:   - Azure DevOpsとの統合
   - PoC環境用の低コスト設計
   - 開発・テスト・本番環境の段階的展開計画

2. **優先順位付け**:   - インフラ基盤構築タスクと同期
   - 初期段階ではインフラ基盤構築を優先アの責任範囲明確化
   - 必要最小限のリソースからスタートし徐々に拡張

3. **CI/CDパイプライン**:   - 低コスト構成から本番移行時の構成変更複雑化に対応
   - Azure DevOpsとの統合象化とモジュール化
   - 環境ごとの自動デプロイメント

4. **スプリント計画**:
   - インフラ基盤構築タスクと同期
   - DevOpsエンジニアの責任範囲明確化   - すべての主要リソースをモジュール化し、再利用性を高める
て機能するように設計
5. **リスク管理**:
   - 低コスト構成から本番移行時の構成変更複雑化に対応
   - インフラ構成の抽象化とモジュール化
   - 一貫した命名規則を使用（例：リソース種別-プロジェクト名-環境）
## 10. 推奨事項とベストプラクティスたリソース分類と属性管理

1. **モジュール設計**
   - すべての主要リソースをモジュール化し、再利用性を高める
   - モジュールは独立して機能するように設計   - Terraformとプロバイダーのバージョンを固定
   - インターフェースを明確に定義し、バージョニングを管理アップデートのスケジュール設定

2. **命名規則**
   - 一貫した命名規則を使用（例：リソース種別-プロジェクト名-環境）
   - タグを使用したリソース分類と属性管理   - 自動スケーリング設定による効率的なリソース使用
   - リソース名の最大長を考慮した略称の一貫した適用コスト分析とアラート設定

3. **バージョン管理**ブと削除
   - Terraformとプロバイダーのバージョンを固定
   - 定期的なバージョンアップデートのスケジュール設定
   - バージョンアップグレードの影響評価プロセスの確立   - 段階的デプロイによるリスク低減
囲の事前評価
4. **運用効率**
   - 自動スケーリング設定による効率的なリソース使用
   - タグベースのコスト分析とアラート設定
   - 開発環境では不要な時間帯のリソース削減を自動化
本文書では、産業機器ログ収集・分析プラットフォーム（MachineLog）のインフラストラクチャをTerraformで実装するための詳細な設計を提供しました。この設計に従うことで、複数環境にわたる一貫したインフラストラクチャのデプロイが可能になります。また、セキュリティ、可用性、コスト最適化を考慮した構成となっており、実装計画と整合した形で環境を段階的に拡張できます。イフサイクル管理による自動アーカイブと削除
   - 段階的デプロイによるリスク低減
   - 変更の影響範囲の事前評価
   - ロールバック戦略の事前定義

## まとめ

本文書では、産業機器ログ収集・分析プラットフォーム（MachineLog）のインフラストラクチャをTerraformで実装するための詳細な設計を提供しました。この設計に従うことで、複数環境にわたる一貫したインフラストラクチャのデプロイが可能になります。また、セキュリティ、可用性、コスト最適化を考慮した構成となっており、実装計画と整合した形で環境を段階的に拡張できます。
