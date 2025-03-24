variable "resource_group_name" {
  description = "リソースグループの名前"
  type        = string
}

variable "location" {
  description = "リソースのデプロイ先リージョン"
  type        = string
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

variable "allowed_subnet_ids" {
  description = "許可するサブネットIDのリスト"
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
