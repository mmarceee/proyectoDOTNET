# Proveedores del entorno productivo: DigitalOcean (App Platform, PostgreSQL y Valkey administrados), CloudAMQP
# (RabbitMQ) y random (claves y contraseña generadas). El estado queda local, en terraform.tfstate, fuera de Git:
# guarda secretos (README de esta carpeta).

terraform {
  required_version = ">= 1.6"

  required_providers {
    digitalocean = {
      source  = "digitalocean/digitalocean"
      version = "~> 2.0"
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

provider "digitalocean" {
  token = var.digitalocean_token
}

provider "cloudamqp" {
  apikey = var.cloudamqp_apikey
}
