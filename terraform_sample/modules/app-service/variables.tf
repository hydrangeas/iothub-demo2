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

variable "app_service_plan_sku" {
  description = "App Serviceプランのスキュー"
  type        = string
  default     = "B1"
}

variable "aspnet_environment" {
  description = "ASP.NET Core環境"
  type        = string
  default     = "Production"
}

variable "application_insights_connection_string" {
  description = "Application Insightsの接続文字列"
  type        = string
  default     = ""
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
  description = "App Serviceを統合するサブネットのID"
  type        = string
  default     = null
}

variable "tags" {
  description = "リソースに付与するタグ"
  type        = map(string)
  default     = {}
}
