variable "resource_group_name" {
  description = "リソースグループの名前"
  type        = string
}

variable "create_resource_group" {
  description = "リソースグループを作成するかどうか"
  type        = bool
  default     = false
}

variable "location" {
  description = "リソースのデプロイ先リージョン"
  type        = string
}

variable "environment" {
  description = "環境（dev, test, prod）"
  type        = string
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
}

variable "tags" {
  description = "リソースに付与するタグ"
  type        = map(string)
  default     = {}
}

variable "alert_email_address" {
  description = "アラート通知を送信するメールアドレス"
  type        = string
  default     = "admin@example.com"
}
