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

variable "storage_connection_string" {
  description = "ストレージアカウントの接続文字列"
  type        = string
  sensitive   = true
}

variable "storage_container_id" {
  description = "ストレージコンテナのID"
  type        = string
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
