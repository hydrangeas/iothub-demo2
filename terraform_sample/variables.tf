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
    condition     = contains(["LRS", "GRS", "RAGRS", "ZRS"], var.storage_replication_type)
    error_message = "ストレージアカウントのレプリケーションタイプは「LRS」、「GRS」、「RAGRS」、または「ZRS」のいずれかである必要があります。"
  }
}

variable "app_service_plan_sku" {
  description = "App Serviceプランのスキュー"
  type        = string
  default     = "B1"
}

variable "tags" {
  description = "リソースに付与するタグ"
  type        = map(string)
  default     = {}
}

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
