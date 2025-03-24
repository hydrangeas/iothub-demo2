variable "resource_group_name" {
  description = "リソースグループの名前"
  type        = string
}

variable "location" {
  description = "リソースのデプロイ先リージョン"
  type        = string
  default     = "global"
}

variable "environment" {
  description = "環境（dev, test, prod）"
  type        = string
}

variable "application_name" {
  description = "アプリケーションの名前"
  type        = string
  default     = "machinelog"
}

variable "backend_address" {
  description = "バックエンドのアドレス（例：app-machinelog-prod.azurewebsites.net）"
  type        = string
}

variable "custom_domain" {
  description = "カスタムドメイン名（例：app.example.com）"
  type        = string
  default     = null
}

variable "waf_enabled" {
  description = "WAFを有効にするかどうか"
  type        = bool
  default     = true
}

variable "blocked_ip_addresses" {
  description = "ブロックするIPアドレスのリスト"
  type        = list(string)
  default     = []
}

variable "log_analytics_workspace_id" {
  description = "Log AnalyticsワークスペースのID"
  type        = string
}

variable "tags" {
  description = "リソースに付与するタグ"
  type        = map(string)
  default     = {}
}
