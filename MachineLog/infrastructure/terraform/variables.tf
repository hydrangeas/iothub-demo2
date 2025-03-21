# MachineLog インフラストラクチャ定義
# 変数定義ファイル

# 基本設定
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

variable "secondary_location" {
  description = "セカンダリAzureリージョン（災害復旧用）"
  type        = string
  default     = "japanwest"
}

# タグ
variable "tags" {
  description = "リソースに適用するタグ"
  type        = map(string)
  default     = {}
}

# IoT Hub設定
variable "iot_hub_sku" {
  description = "IoT Hub SKU"
  type        = string
  default     = "S1"
}

variable "iot_hub_capacity" {
  description = "IoT Hub容量（ユニット数）"
  type        = number
  default     = 1
}

variable "iot_hub_partition_count" {
  description = "IoT Hubパーティション数"
  type        = number
  default     = 4
}

variable "iot_hub_retention_days" {
  description = "IoT Hubメッセージ保持日数"
  type        = number
  default     = 7
}

# ストレージ設定
variable "storage_account_tier" {
  description = "ストレージアカウント階層"
  type        = string
  default     = "Standard"
}

variable "storage_account_replication_type" {
  description = "ストレージアカウントレプリケーションタイプ"
  type        = string
  default     = "GRS"
}

variable "storage_account_kind" {
  description = "ストレージアカウント種類"
  type        = string
  default     = "StorageV2"
}

variable "storage_access_tier" {
  description = "ストレージアクセス階層"
  type        = string
  default     = "Hot"
}

# App Service設定
variable "app_service_sku_tier" {
  description = "App Service SKU階層"
  type        = string
  default     = "PremiumV3"
}

variable "app_service_sku_size" {
  description = "App Service SKUサイズ"
  type        = string
  default     = "P1v3"
}

variable "app_service_capacity" {
  description = "App Service初期インスタンス数"
  type        = number
  default     = 2
}

variable "app_service_max_capacity" {
  description = "App Service最大インスタンス数"
  type        = number
  default     = 10
}

# Key Vault設定
variable "key_vault_sku" {
  description = "Key Vault SKU"
  type        = string
  default     = "standard"
}

variable "key_vault_soft_delete_retention_days" {
  description = "Key Vaultソフト削除保持日数"
  type        = number
  default     = 90
}

# 監視設定
variable "log_analytics_retention_days" {
  description = "Log Analytics保持日数"
  type        = number
  default     = 90
}

variable "app_insights_sampling_percentage" {
  description = "Application Insightsサンプリング率（%）"
  type        = number
  default     = 10
}

# ネットワーク設定
variable "vnet_address_space" {
  description = "仮想ネットワークアドレス空間"
  type        = list(string)
  default     = ["10.0.0.0/16"]
}

variable "subnet_app_service" {
  description = "App Service用サブネット"
  type        = string
  default     = "10.0.1.0/24"
}

variable "subnet_database" {
  description = "データベース用サブネット"
  type        = string
  default     = "10.0.2.0/24"
}

variable "subnet_integration" {
  description = "統合用サブネット"
  type        = string
  default     = "10.0.3.0/24"
}

# セキュリティ設定
variable "allowed_ip_ranges" {
  description = "許可するIPアドレス範囲"
  type        = list(string)
  default     = []
}

variable "enable_ddos_protection" {
  description = "DDoS保護を有効にするかどうか"
  type        = bool
  default     = true
}

# スケーリング設定
variable "cpu_threshold_percentage" {
  description = "CPUスケーリング閾値（%）"
  type        = number
  default     = 70
}

variable "memory_threshold_percentage" {
  description = "メモリスケーリング閾値（%）"
  type        = number
  default     = 80
}
