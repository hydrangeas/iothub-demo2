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

variable "tags" {
  description = "リソースに付与するタグ"
  type        = map(string)
  default     = {}
}
