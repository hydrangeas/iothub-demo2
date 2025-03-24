output "application_id" {
  description = "アプリケーションのID"
  value       = azuread_application.this.id
}

output "client_id" {
  description = "アプリケーションのクライアントID"
  value       = azuread_application.this.client_id
}

output "object_id" {
  description = "アプリケーションのオブジェクトID"
  value       = azuread_application.this.object_id
}

output "service_principal_id" {
  description = "サービスプリンシパルのID"
  value       = azuread_service_principal.this.id
}

output "service_principal_object_id" {
  description = "サービスプリンシパルのオブジェクトID"
  value       = azuread_service_principal.this.object_id
}

output "client_secret" {
  description = "アプリケーションのクライアントシークレット"
  value       = var.create_client_secret ? azuread_application_password.this[0].value : null
  sensitive   = true
}
