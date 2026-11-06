# Proveedores del entorno productivo: Azure (Container Apps y PostgreSQL Flexible Server, con el crédito de Azure for
# Students) y random (claves y contraseñas generadas). El estado queda local, en terraform.tfstate, fuera de Git:
# guarda secretos (README de esta carpeta).

terraform {
  required_version = ">= 1.6"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}

# Se autentica con la sesión de la CLI de Azure (az login).
provider "azurerm" {
  features {}
  subscription_id = var.azure_subscription_id
}
