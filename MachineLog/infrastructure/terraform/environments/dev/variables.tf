variable "resource_group_name" {
  description = "リソースグループの名前"
  type        = string
  default     = "rg-machinelog-dev"
}

variable "location" {
  description = "リソースのデプロイ先リージョン"
  type        = string
  default     = "japaneast"
}

variable "application_name" {
  description = "アプリケーションの名前"
  type        = string
  default     = "machinelog"
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
  default     = 30
}

variable "alert_email_address" {
  description = "アラート通知を送信するメールアドレス"
  type        = string
  default     = "admin@example.com"
}

# ストレージ関連設定
variable "storage_account_tier" {
  description = "ストレージアカウントの階層"
  type        = string
  default     = "Standard"
}

variable "storage_replication_type" {
  description = "ストレージアカウントのレプリケーションタイプ"
  type        = string
  default     = "LRS"
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

# IoT Hub関連設定
variable "iot_hub_sku" {
  description = "IoT HubのSKU"
  type        = string
  default     = "S1"
}

variable "iot_hub_capacity" {
  description = "IoT Hubのユニット数"
  type        = number
  default     = 1
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
  default     = "B1"
}

# Function App関連設定
variable "use_consumption_plan" {
  description = "Function AppでConsumptionプランを使用するかどうか"
  type        = bool
  default     = true
}
