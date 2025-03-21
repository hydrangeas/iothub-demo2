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

variable "log_retention_in_days" {
  description = "ログの保持期間（日数）"
  type        = number
  default     = 365
}

variable "enable_network_rules" {
  description = "ネットワークルールを有効にするかどうか"
  type        = bool
  default     = false
}

variable "allowed_ip_ranges" {
  description = "許可するIPアドレス範囲のリスト"
  type        = list(string)
  default     = []
}

variable "allowed_subnet_ids" {
  description = "許可するサブネットIDのリスト"
  type        = list(string)
  default     = []
}

variable "tags" {
  description = "リソースに付与するタグ"
  type        = map(string)
  default     = {}
}
