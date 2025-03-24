resource "azurerm_virtual_network" "this" {
  name                = "vnet-${var.application_name}-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  address_space       = var.address_space
  tags                = var.tags
}

resource "azurerm_subnet" "subnets" {
  count                = length(var.subnet_names)
  name                 = var.subnet_names[count.index]
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.subnet_prefixes[count.index]]

  # App Service用のサブネットの場合、委任を設定
  dynamic "delegation" {
    for_each = var.subnet_names[count.index] == "app-subnet" ? [1] : []
    content {
      name = "app-service-delegation"
      service_delegation {
        name    = "Microsoft.Web/serverFarms"
        actions = ["Microsoft.Network/virtualNetworks/subnets/action"]
      }
    }
  }

  # Function App用のサブネットの場合、委任を設定
  dynamic "delegation" {
    for_each = var.subnet_names[count.index] == "function-subnet" ? [1] : []
    content {
      name = "function-app-delegation"
      service_delegation {
        name    = "Microsoft.Web/serverFarms"
        actions = ["Microsoft.Network/virtualNetworks/subnets/action"]
      }
    }
  }

  service_endpoints = [
    "Microsoft.Storage",
    "Microsoft.Sql",
    "Microsoft.Web"
  ]
}

# ネットワークセキュリティグループの作成
resource "azurerm_network_security_group" "app" {
  name                = "nsg-app-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags

  security_rule {
    name                       = "AllowHTTPSInbound"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "443"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }

  security_rule {
    name                       = "DenyAllInbound"
    priority                   = 1000
    direction                  = "Inbound"
    access                     = "Deny"
    protocol                   = "*"
    source_port_range          = "*"
    destination_port_range     = "*"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }
}

# NSGとサブネットの関連付け
resource "azurerm_subnet_network_security_group_association" "app" {
  subnet_id                 = azurerm_subnet.subnets[index(var.subnet_names, "app-subnet")].id
  network_security_group_id = azurerm_network_security_group.app.id
}

# プライベートDNSゾーンの作成
resource "azurerm_private_dns_zone" "storage" {
  name                = "privatelink.blob.core.windows.net"
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_private_dns_zone" "cosmos" {
  name                = "privatelink.documents.azure.com"
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

# プライベートDNSゾーンとVNetのリンク
resource "azurerm_private_dns_zone_virtual_network_link" "storage" {
  name                  = "link-storage-${var.environment}"
  resource_group_name   = var.resource_group_name
  private_dns_zone_name = azurerm_private_dns_zone.storage.name
  virtual_network_id    = azurerm_virtual_network.this.id
  tags                  = var.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "cosmos" {
  name                  = "link-cosmos-${var.environment}"
  resource_group_name   = var.resource_group_name
  private_dns_zone_name = azurerm_private_dns_zone.cosmos.name
  virtual_network_id    = azurerm_virtual_network.this.id
  tags                  = var.tags
}
