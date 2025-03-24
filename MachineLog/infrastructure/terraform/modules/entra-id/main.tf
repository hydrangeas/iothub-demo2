resource "azuread_application" "this" {
  display_name = "${var.application_name}-${var.environment}"

  web {
    homepage_url  = var.homepage_url
    redirect_uris = var.redirect_uris

    implicit_grant {
      access_token_issuance_enabled = true
      id_token_issuance_enabled     = true
    }
  }

  api {
    requested_access_token_version = 2

    oauth2_permission_scope {
      admin_consent_description  = "Allow the application to access ${var.application_name} on behalf of the signed-in user."
      admin_consent_display_name = "Access ${var.application_name}"
      enabled                    = true
      id                         = "00000000-0000-0000-0000-000000000001"
      type                       = "User"
      user_consent_description   = "Allow the application to access ${var.application_name} on your behalf."
      user_consent_display_name  = "Access ${var.application_name}"
      value                      = "user_impersonation"
    }
  }

  required_resource_access {
    resource_app_id = "00000003-0000-0000-c000-000000000000" # Microsoft Graph

    resource_access {
      id   = "e1fe6dd8-ba31-4d61-89e7-88639da4683d" # User.Read
      type = "Scope"
    }
  }

  app_role {
    allowed_member_types = ["User"]
    description          = "Administrators can manage all aspects of the application"
    display_name         = "Administrator"
    enabled              = true
    id                   = "00000000-0000-0000-0000-000000000002"
    value                = "Admin"
  }

  app_role {
    allowed_member_types = ["User"]
    description          = "Readers can view data but not modify"
    display_name         = "Reader"
    enabled              = true
    id                   = "00000000-0000-0000-0000-000000000003"
    value                = "Reader"
  }
}

resource "azuread_service_principal" "this" {
  client_id = azuread_application.this.client_id
}

resource "azuread_application_password" "this" {
  count             = var.create_client_secret ? 1 : 0
  application_id    = azuread_application.this.id
  display_name      = "terraform-generated"
  end_date_relative = "8760h" # 1 year
}
