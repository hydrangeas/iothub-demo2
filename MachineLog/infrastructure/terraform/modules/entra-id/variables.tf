variable "application_name" {
  description = "アプリケーションの名前"
  type        = string
}

variable "environment" {
  description = "環境（dev, test, prod）"
  type        = string
}

variable "homepage_url" {
  description = "アプリケーションのホームページURL"
  type        = string
  default     = "https://localhost:5001"
}

variable "redirect_uris" {
  description = "リダイレクトURIのリスト"
  type        = list(string)
  default     = ["https://localhost:5001/signin-oidc"]
}

variable "create_client_secret" {
  description = "クライアントシークレットを作成するかどうか"
  type        = bool
  default     = true
}
