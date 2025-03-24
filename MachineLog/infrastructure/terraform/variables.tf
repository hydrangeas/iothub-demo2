variable "resource_group_name" {
  description = "リソースグループの名前"
  type        = string
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

variable "application_name" {
  description = "アプリケーションの名前"
  type        = string
  default     = "machinelog"
}

variable "log_analytics_sku" {
  description = "Log Analyticsワークスペースのスキュー"
  type        = string
  default     = "PerGB2018"
}

variable "log_retention_in_days" {
  description = "ログの保持期間（日数）"
  type        = number
  default     = 30
  validation {
    condition     = var.log_retention_in_days >= 30 && var.log_retention_in_days <= 730
    error_message = "ログの保持期間は30日から730日の間である必要があります。"
  }
}

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
  default     = "LRS"
  validation {
    condition     = contains(["LRS", "GRS", "RAGRS", "ZRS", "GZRS", "RAGZRS"], var.storage_replication_type)
    error_message = "ストレージアカウントのレプリケーションタイプは有効な値である必要があります。"
  }
}

variable "blob_soft_delete_retention_days" {
  description = "BLOBの論理削除保持期間（日数）"
  type        = number
  default     = 7
}

variable "container_soft_delete_retention_days" {
  description = "コンテナの論理削除保持期間（日数）"
  type        = number
  default     = 7
}

variable "app_service_plan_sku" {
  description = "App Serviceプランのスキュー"
  type        = string
  default     = "B1"
}

variable "alert_email_address" {
  description = "アラート通知を送信するメールアドレス"
  type        = string
  default     = "admin@example.com"
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

# IoT Hub関連設定
variable "iot_hub_sku" {
  description = "IoT HubのSKU"
  type        = string
  default     = "S1"
  validation {
    condition     = contains(["F1", "S1", "S2", "S3"], var.iot_hub_sku)
    error_message = "IoT HubのSKUは「F1」、「S1」、「S2」、または「S3」のいずれかである必要があります。"
  }
}

variable "iot_hub_capacity" {
  description = "IoT Hubのユニット数"
  type        = number
  default     = 1
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

# Function App関連設定
variable "use_consumption_plan" {
  description = "Function AppでConsumptionプランを使用するかどうか"
  type        = bool
  default     = false
}

# Front Door関連設定は最新のアーキテクチャ設計から削除されました

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
