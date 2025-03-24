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

variable "storage_account_name" {
  description = "Function App用ストレージアカウントの名前"
  type        = string
}

variable "storage_account_access_key" {
  description = "Function App用ストレージアカウントのアクセスキー"
  type        = string
  sensitive   = true
}

variable "function_app_plan_sku" {
  description = "Function Appプランのスキュー"
  type        = string
  default     = "EP1"
}

variable "use_consumption_plan" {
  description = "Consumptionプランを使用するかどうか"
  type        = bool
  default     = false
}

variable "app_service_plan_id" {
  description = "既存のApp Serviceプランを使用する場合のID"
  type        = string
  default     = null
}

variable "application_insights_connection_string" {
  description = "Application Insightsの接続文字列"
  type        = string
  default     = ""
}

variable "application_insights_key" {
  description = "Application Insightsのインストルメンテーションキー"
  type        = string
  default     = ""
}

variable "cosmos_db_connection_string" {
  description = "Cosmos DBの接続文字列"
  type        = string
  sensitive   = true
}

variable "iot_hub_connection_string" {
  description = "IoT Hubの接続文字列"
  type        = string
  sensitive   = true
}

variable "log_analytics_workspace_id" {
  description = "Log Analyticsワークスペースの一意識別子"
  type        = string
}

variable "log_analytics_workspace_resource_id" {
  description = "Log AnalyticsワークスペースのAzureリソースID"
  type        = string
}

variable "tenant_id" {
  description = "Microsoft Entra IDテナントID"
  type        = string
}

variable "client_id" {
  description = "アプリケーションのクライアントID"
  type        = string
}

variable "subnet_id" {
  description = "Function Appを統合するサブネットのID"
  type        = string
  default     = null
}

variable "tags" {
  description = "リソースに付与するタグ"
  type        = map(string)
  default     = {}
}
