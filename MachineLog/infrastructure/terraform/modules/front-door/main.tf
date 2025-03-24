resource "azurerm_frontdoor" "this" {
  name                = "fd-${var.application_name}-${var.environment}"
  resource_group_name = var.resource_group_name
  tags                = var.tags

  routing_rule {
    name               = "default-routing-rule"
    accepted_protocols = ["Http", "Https"]
    patterns_to_match  = ["/*"]
    frontend_endpoints = ["default-frontend-endpoint"]
    forwarding_configuration {
      forwarding_protocol = "HttpsOnly"
      backend_pool_name   = "default-backend-pool"
    }
  }

  backend_pool_load_balancing {
    name = "default-load-balancing"
  }

  backend_pool_health_probe {
    name                = "default-health-probe"
    protocol            = "Https"
    path                = "/health"
    interval_in_seconds = 30
  }

  backend_pool {
    name = "default-backend-pool"
    backend {
      host_header = var.backend_address
      address     = var.backend_address
      http_port   = 80
      https_port  = 443
      weight      = 100
      priority    = 1
    }

    load_balancing_name = "default-load-balancing"
    health_probe_name   = "default-health-probe"
  }

  frontend_endpoint {
    name                                    = "default-frontend-endpoint"
    host_name                               = "fd-${var.application_name}-${var.environment}.azurefd.net"
    web_application_firewall_policy_link_id = var.waf_enabled ? azurerm_frontdoor_firewall_policy.this[0].id : null
  }

  # カスタムドメインがある場合
  dynamic "frontend_endpoint" {
    for_each = var.custom_domain != null ? [1] : []
    content {
      name                                    = "custom-frontend-endpoint"
      host_name                               = var.custom_domain
      web_application_firewall_policy_link_id = var.waf_enabled ? azurerm_frontdoor_firewall_policy.this[0].id : null
    }
  }
}

# WAFポリシー（有効な場合）
resource "azurerm_frontdoor_firewall_policy" "this" {
  count               = var.waf_enabled ? 1 : 0
  name                = "wafpolicy-${var.application_name}-${var.environment}"
  resource_group_name = var.resource_group_name
  tags                = var.tags

  custom_rule {
    name                           = "BlockIPAddresses"
    enabled                        = true
    priority                       = 100
    rate_limit_duration_in_minutes = 1
    rate_limit_threshold           = 10
    type                           = "MatchRule"
    action                         = "Block"

    match_condition {
      match_variable     = "RemoteAddr"
      operator           = "IPMatch"
      negation_condition = false
      match_values       = var.blocked_ip_addresses
    }
  }

  managed_rule {
    type    = "DefaultRuleSet"
    version = "1.0"
  }

  managed_rule {
    type    = "Microsoft_BotManagerRuleSet"
    version = "1.0"
  }
}

# Front Door診断設定
resource "azurerm_monitor_diagnostic_setting" "frontdoor" {
  name                       = "diag-${azurerm_frontdoor.this.name}"
  target_resource_id         = azurerm_frontdoor.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }

  metric {
    category = "AllMetrics"
    enabled  = true
  }
}
