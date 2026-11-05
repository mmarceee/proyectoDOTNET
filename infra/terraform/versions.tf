# Proveedores del entorno productivo: Azure (Container Apps y PostgreSQL Flexible Server, con el crédito de Azure for
# Students), CloudAMQP (RabbitMQ) y random (claves y contraseñas generadas). El estado queda local, en
# terraform.tfstate, fuera de Git: guarda secretos (README de esta carpeta).

terraform {
  required_version = ">= 1.6"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    cloudamqp = {
      source  = "cloudamqp/cloudamqp"
      version = "~> 1.0"
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

provider "cloudamqp" {
  apikey = var.cloudamqp_apikey
}
