# Entorno productivo (propuesta de stack, sección 6): App Platform con dos componentes, la API (servicio web) y el
# Worker; PostgreSQL y Valkey administrados por DigitalOcean; RabbitMQ en CloudAMQP. Todo se crea con terraform apply
# y se borra con terraform destroy (letra, sección 6.14).

locals {
  nombre = "logistica-${var.entorno}"
}

# --- Claves y contraseña que genera Terraform ------------------------------------------------------------------
# Quedan sólo en el estado y en los secretos de App Platform, nunca en el repositorio. Si se destruye el ambiente se
# generan otras, y la base nueva no tiene nada cifrado con las anteriores.

# Firma de los enlaces del seguimiento público (CU-60).
resource "random_bytes" "clave_token" {
  length = 32
}

# Cifrado de los secretos de avisos a comercios (CU-63). API y Worker usan la misma.
resource "random_bytes" "clave_cifrado_avisos" {
  length = 32
}

# Usuarios de demostración (UsuariosIniciales): el repositorio es público, así que no se usa la del código.
resource "random_password" "demo" {
  length           = 20
  min_upper        = 1
  min_lower        = 1
  min_numeric      = 1
  min_special      = 1
  override_special = "!-_"
}

# --- Datos ------------------------------------------------------------------------------------------------------

resource "digitalocean_database_cluster" "postgres" {
  name       = "${local.nombre}-postgres"
  engine     = "pg"
  version    = "17"
  size       = var.tamano_base
  region     = var.region_datos
  node_count = 1
}

resource "digitalocean_database_db" "logistica" {
  cluster_id = digitalocean_database_cluster.postgres.id
  name       = "logistica"
}

resource "digitalocean_database_cluster" "valkey" {
  name       = "${local.nombre}-valkey"
  engine     = "valkey"
  version    = "8"
  size       = var.tamano_base
  region     = var.region_datos
  node_count = 1
  # Es una caché: con la memoria llena descarta las claves menos usadas en lugar de rechazar escrituras (ADR-0006).
  eviction_policy = "allkeys_lru"
}

resource "cloudamqp_instance" "rabbitmq" {
  name   = "${local.nombre}-rabbitmq"
  plan   = "lemur" # gratuito, RabbitMQ compartido
  region = var.region_rabbitmq
  tags   = [local.nombre]
}

locals {
  # 25 conexiones en el nodo más chico, 3 reservadas: 12 para la API y 6 para el Worker, con margen para psql.
  postgres = join(";", [
    "Host=${digitalocean_database_cluster.postgres.host}",
    "Port=${digitalocean_database_cluster.postgres.port}",
    "Database=${digitalocean_database_db.logistica.name}",
    "Username=${digitalocean_database_cluster.postgres.user}",
    "Password=${digitalocean_database_cluster.postgres.password}",
    "SSL Mode=Require",
  ])

  # Valkey de DigitalOcean sólo acepta TLS.
  valkey = "${digitalocean_database_cluster.valkey.host}:${digitalocean_database_cluster.valkey.port},password=${digitalocean_database_cluster.valkey.password},ssl=true"

  # Variables comunes a la API y el Worker. Las cadenas de conexión y las claves van como secretos.
  entorno_comun = {
    "ConnectionStrings__Valkey"         = { valor = local.valkey, secreto = true }
    "ConnectionStrings__RabbitMQ"       = { valor = cloudamqp_instance.rabbitmq.url, secreto = true }
    "ConnectionStrings__Smtp"           = { valor = var.smtp, secreto = true }
    "Seguimiento__ClaveToken"           = { valor = random_bytes.clave_token.base64, secreto = true }
    "Seguimiento__Avisos__ClaveCifrado" = { valor = random_bytes.clave_cifrado_avisos.base64, secreto = true }
    "Seguimiento__Correo__Remitente"    = { valor = var.correo_remitente, secreto = false }
    # App Platform reemplaza ${APP_URL} por la dirección pública de la aplicación (enlaces de los correos, CU-62).
    "Aplicacion__UrlPublica" = { valor = "$${APP_URL}", secreto = false }
  }

  entorno_api = merge(local.entorno_comun, {
    "ASPNETCORE_ENVIRONMENT" = { valor = "Production", secreto = false }
    # App Platform termina HTTPS y reenvía por HTTP: la API toma el esquema y la IP del cliente de X-Forwarded-*.
    "ASPNETCORE_FORWARDEDHEADERS_ENABLED" = { valor = "true", secreto = false }
    # Una sola instancia (exención del escalado horizontal): migra y siembra al iniciar, como en Docker Compose.
    "Database__MigrateOnStartup"  = { valor = "true", secreto = false }
    "ConnectionStrings__Postgres" = { valor = "${local.postgres};Maximum Pool Size=12", secreto = true }
    "Demo__Contrasena"            = { valor = random_password.demo.result, secreto = true }
  })

  entorno_worker = merge(local.entorno_comun, {
    "DOTNET_ENVIRONMENT"          = { valor = "Production", secreto = false }
    "ConnectionStrings__Postgres" = { valor = "${local.postgres};Maximum Pool Size=6", secreto = true }
  })
}

# --- Aplicación -------------------------------------------------------------------------------------------------
# App Platform compila las imágenes con los Dockerfile del repositorio. No despliega con cada push: lo pide el CI
# después de que pasan las pruebas (.github/workflows/ci.yml, job deploy).

resource "digitalocean_app" "logistica" {
  spec {
    name   = local.nombre
    region = var.region_app

    service {
      name               = "api"
      instance_count     = 1
      instance_size_slug = var.tamano_api
      source_dir         = "/"
      dockerfile_path    = "src/Logistica.Api/Dockerfile"
      http_port          = 8080

      github {
        repo           = var.repositorio
        branch         = var.rama
        deploy_on_push = false
      }

      # Las migraciones corren antes de que la API atienda: se le da tiempo antes del primer chequeo.
      health_check {
        http_path             = "/health"
        initial_delay_seconds = 30
        period_seconds        = 10
        failure_threshold     = 6
      }

      dynamic "env" {
        for_each = local.entorno_api
        content {
          key   = env.key
          value = env.value.valor
          scope = "RUN_TIME"
          type  = env.value.secreto ? "SECRET" : "GENERAL"
        }
      }
    }

    worker {
      name               = "worker"
      instance_count     = 1
      instance_size_slug = var.tamano_worker
      source_dir         = "/"
      dockerfile_path    = "src/Logistica.Worker/Dockerfile"

      github {
        repo           = var.repositorio
        branch         = var.rama
        deploy_on_push = false
      }

      dynamic "env" {
        for_each = local.entorno_worker
        content {
          key   = env.key
          value = env.value.valor
          scope = "RUN_TIME"
          type  = env.value.secreto ? "SECRET" : "GENERAL"
        }
      }
    }

    # Un solo origen (ADR-0001, addendum 5): todo el tráfico, con HTTPS y certificado de App Platform, va a la API.
    ingress {
      rule {
        component {
          name = "api"
        }
        match {
          path {
            prefix = "/"
          }
        }
      }
    }
  }
}

# --- Acceso a los datos -----------------------------------------------------------------------------------------
# PostgreSQL y Valkey sólo aceptan conexiones de la aplicación y de las IP de administración que se indiquen.

resource "digitalocean_database_firewall" "postgres" {
  cluster_id = digitalocean_database_cluster.postgres.id

  rule {
    type  = "app"
    value = digitalocean_app.logistica.id
  }

  dynamic "rule" {
    for_each = var.ips_administracion
    content {
      type  = "ip_addr"
      value = rule.value
    }
  }
}

resource "digitalocean_database_firewall" "valkey" {
  cluster_id = digitalocean_database_cluster.valkey.id

  rule {
    type  = "app"
    value = digitalocean_app.logistica.id
  }

  dynamic "rule" {
    for_each = var.ips_administracion
    content {
      type  = "ip_addr"
      value = rule.value
    }
  }
}

# Agrupa los recursos de DigitalOcean: el panel del proyecto muestra su costo mensual (evidencia de costo, 6.14).
resource "digitalocean_project" "logistica" {
  name        = local.nombre
  description = "Laboratorio .NET 2026, Equipo 1: plataforma multioperador de última milla."
  purpose     = "Web Application"
  environment = "Production"
  resources = [
    digitalocean_app.logistica.urn,
    digitalocean_database_cluster.postgres.urn,
    digitalocean_database_cluster.valkey.urn,
  ]
}
