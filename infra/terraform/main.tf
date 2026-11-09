# Entorno productivo (propuesta de stack, sección 6) en Azure, con el crédito de Azure for Students: Container Apps con
# la API (entrada pública con HTTPS), el Worker, Valkey y RabbitMQ, y PostgreSQL Flexible Server administrado.
# Todo se crea con terraform apply y se borra con terraform destroy (letra, sección 6.14).

locals {
  nombre          = "logistica-${var.entorno}"
  ubicacion_pg    = var.ubicacion_postgres == "" ? var.ubicacion : var.ubicacion_postgres
  usa_registro    = nonsensitive(var.ghcr_token != "")
  nombre_api      = "${local.nombre}-api"
  nombre_worker   = "${local.nombre}-worker"
  nombre_valkey   = "${local.nombre}-valkey"
  nombre_rabbitmq = "${local.nombre}-rabbitmq"
  nombre_aspire   = "${local.nombre}-aspire"
  perfil_trabajo  = "Consumption"
}

resource "azurerm_resource_group" "this" {
  name     = local.nombre
  location = var.ubicacion
}

# Permiso del despliegue continuo (job deploy del CI) sobre el grupo de recursos. Va acá y no a mano porque destroy
# borra el grupo y, con él, la asignación: cada apply la vuelve a crear.
resource "azurerm_role_assignment" "ci" {
  count                = var.ci_principal_id == "" ? 0 : 1
  scope                = azurerm_resource_group.this.id
  role_definition_name = "Contributor"
  principal_id         = var.ci_principal_id
  principal_type       = "ServicePrincipal"
}

# --- Claves y contraseñas que genera Terraform ------------------------------------------------------------------
# Quedan sólo en el estado y en los secretos de Container Apps, nunca en el repositorio. Si se destruye el ambiente se
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

# Sin caracteres que rompan la cadena de conexión de Npgsql (el punto y coma la separa).
resource "random_password" "postgres" {
  length           = 24
  override_special = "-_"
}

# Usuario de RabbitMQ. Sin caracteres especiales: va dentro de la URL amqp://usuario:contraseña@...
resource "random_password" "rabbitmq" {
  length  = 24
  special = false
}

# Aspire Dashboard: el token para entrar al panel y la clave con que la API y el Worker envían la telemetría.
resource "random_password" "aspire_token" {
  length  = 24
  special = false
}

resource "random_password" "aspire_otlp" {
  length  = 32
  special = false
}

# El nombre del servidor de PostgreSQL es único en todo Azure.
resource "random_string" "sufijo" {
  length  = 6
  upper   = false
  special = false
}

# --- Datos ------------------------------------------------------------------------------------------------------

resource "azurerm_postgresql_flexible_server" "this" {
  name                          = "${local.nombre}-pg-${random_string.sufijo.result}"
  resource_group_name           = azurerm_resource_group.this.name
  location                      = local.ubicacion_pg
  version                       = "16"
  sku_name                      = "B_Standard_B1ms" # el más chico; gratuito 12 meses en las cuentas que lo incluyen
  storage_mb                    = 32768
  backup_retention_days         = 7
  administrator_login           = "logistica"
  administrator_password        = random_password.postgres.result
  public_network_access_enabled = true

  # Azure elige la zona al crear el servidor; no se vuelve a tocar.
  lifecycle {
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_database" "logistica" {
  name      = "logistica"
  server_id = azurerm_postgresql_flexible_server.this.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

# 0.0.0.0 es la regla especial de Azure para los servicios de Azure: Container Apps sale por direcciones de Azure.
resource "azurerm_postgresql_flexible_server_firewall_rule" "azure" {
  name             = "servicios-de-azure"
  server_id        = azurerm_postgresql_flexible_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "administracion" {
  count            = var.ip_administracion == "" ? 0 : 1
  name             = "administracion"
  server_id        = azurerm_postgresql_flexible_server.this.id
  start_ip_address = var.ip_administracion
  end_ip_address   = var.ip_administracion
}

# --- Entorno de Container Apps ----------------------------------------------------------------------------------
# En una red propia: Valkey y RabbitMQ se exponen por TCP sólo dentro del entorno, y Container Apps exige una red
# propia para eso. La red no tiene costo.

resource "azurerm_virtual_network" "this" {
  name                = local.nombre
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  address_space       = ["10.40.0.0/16"]
}

resource "azurerm_subnet" "apps" {
  name                 = "container-apps"
  resource_group_name  = azurerm_resource_group.this.name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = ["10.40.0.0/23"]

  delegation {
    name = "container-apps"
    service_delegation {
      name    = "Microsoft.App/environments"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

# Los logs de los contenedores (la salida de consola de la API y el Worker), para ver un despliegue que falla.
resource "azurerm_log_analytics_workspace" "this" {
  name                = local.nombre
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_container_app_environment" "this" {
  name                       = local.nombre
  resource_group_name        = azurerm_resource_group.this.name
  location                   = azurerm_resource_group.this.location
  log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
  infrastructure_subnet_id   = azurerm_subnet.apps.id

  workload_profile {
    name                  = local.perfil_trabajo
    workload_profile_type = "Consumption"
  }

  # Azure crea y nombra solo el grupo de recursos de la infraestructura del entorno.
  lifecycle {
    ignore_changes = [infrastructure_resource_group_name]
  }
}

# --- Configuración de la API y el Worker ------------------------------------------------------------------------

locals {
  # La conexión a PostgreSQL exige TLS. El nodo B1ms admite 50 conexiones: 12 para la API y 6 para el Worker.
  postgres = join(";", [
    "Host=${azurerm_postgresql_flexible_server.this.fqdn}",
    "Port=5432",
    "Database=${azurerm_postgresql_flexible_server_database.logistica.name}",
    "Username=${azurerm_postgresql_flexible_server.this.administrator_login}",
    "Password=${random_password.postgres.result}",
    "SSL Mode=Require",
  ])

  # Dirección pública de la API: el dominio del entorno con el nombre de la aplicación.
  url_publica = "https://${local.nombre_api}.${azurerm_container_app_environment.this.default_domain}"

  # Los secretos de Container Apps se nombran en minúsculas y con guiones; las variables de entorno los referencian.
  secretos_comunes = {
    "conexion-valkey"   = "${local.nombre_valkey}:6379"
    "conexion-rabbitmq" = "amqp://logistica:${random_password.rabbitmq.result}@${local.nombre_rabbitmq}:5672/"
    "conexion-smtp"     = var.smtp
    "clave-token"       = random_bytes.clave_token.base64
    "clave-avisos"      = random_bytes.clave_cifrado_avisos.base64
  }

  secretos_registro = local.usa_registro ? { "ghcr-token" = var.ghcr_token } : {}

  secretos_api = merge(local.secretos_comunes, local.secretos_registro, local.secretos_otlp, {
    "conexion-postgres" = "${local.postgres};Maximum Pool Size=12"
    "contrasena-demo"   = random_password.demo.result
  })

  secretos_worker = merge(local.secretos_comunes, local.secretos_registro, local.secretos_otlp, {
    "conexion-postgres" = "${local.postgres};Maximum Pool Size=6"
  })

  # Variable de entorno -> secreto que la alimenta.
  variables_secretas_comunes = {
    "ConnectionStrings__Postgres"       = "conexion-postgres"
    "ConnectionStrings__Valkey"         = "conexion-valkey"
    "ConnectionStrings__RabbitMQ"       = "conexion-rabbitmq"
    "ConnectionStrings__Smtp"           = "conexion-smtp"
    "Seguimiento__ClaveToken"           = "clave-token"
    "Seguimiento__Avisos__ClaveCifrado" = "clave-avisos"
  }

  variables_api = {
    "ASPNETCORE_ENVIRONMENT" = "Production"
    # Container Apps termina HTTPS y reenvía por HTTP: la API toma el esquema y la IP del cliente de X-Forwarded-*.
    "ASPNETCORE_FORWARDEDHEADERS_ENABLED" = "true"
    # Una sola instancia (exención del escalado horizontal): migra y siembra al iniciar, como en Docker Compose.
    "Database__MigrateOnStartup"     = "true"
    "Aplicacion__UrlPublica"         = local.url_publica
    "Seguimiento__Correo__Remitente" = var.correo_remitente
  }

  variables_worker = {
    "DOTNET_ENVIRONMENT"             = "Production"
    "Aplicacion__UrlPublica"         = local.url_publica
    "Seguimiento__Correo__Remitente" = var.correo_remitente
  }

  # Sin el SQL de cada consulta en los logs: el publicador del outbox consulta cada dos segundos y llenaría Log Analytics.
  variables_logs = {
    "Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command" = "Warning"
  }

  # Telemetría hacia Aspire Dashboard por OTLP sobre HTTPS, con la clave en un encabezado. Sin observabilidad, la API
  # y el Worker no exportan nada (Program.cs mira OTEL_EXPORTER_OTLP_ENDPOINT).
  url_aspire = "https://${local.nombre_aspire}.${azurerm_container_app_environment.this.default_domain}"

  secretos_otlp = var.observabilidad ? { "otlp-encabezados" = "x-otlp-api-key=${random_password.aspire_otlp.result}" } : {}

  variables_otlp = var.observabilidad ? {
    "OTEL_EXPORTER_OTLP_ENDPOINT" = local.url_aspire
    "OTEL_EXPORTER_OTLP_PROTOCOL" = "http/protobuf"
    "OTEL_METRIC_EXPORT_INTERVAL" = "10000"
  } : {}

  variables_secretas_otlp = var.observabilidad ? { "OTEL_EXPORTER_OTLP_HEADERS" = "otlp-encabezados" } : {}

  # Proxy delante del panel: Container Apps expone un solo puerto, y Aspire Dashboard atiende el panel y la telemetría
  # en puertos distintos. /v1/* (OTLP sobre HTTP) va al receptor; el resto, al panel.
  caddyfile = <<-EOT
    {
      admin off
      auto_https off
    }
    :8080 {
      handle /v1/* {
        reverse_proxy localhost:18890
      }
      handle {
        reverse_proxy localhost:18888
      }
    }
  EOT
}

# --- Aplicaciones -----------------------------------------------------------------------------------------------
# Una réplica de cada una, siempre encendida: la API ejecuta el publicador del outbox y SignalR, y el Worker consume
# RabbitMQ; no pueden apagarse por falta de tráfico. Las imágenes las publica el CI en GitHub Container Registry, y el
# job deploy actualiza la imagen en cada push a main (por eso Terraform no la vuelve a tocar).

resource "azurerm_container_app" "valkey" {
  name                         = local.nombre_valkey
  resource_group_name          = azurerm_resource_group.this.name
  container_app_environment_id = azurerm_container_app_environment.this.id
  revision_mode                = "Single"
  workload_profile_name        = local.perfil_trabajo

  # Sólo dentro del entorno, por TCP. Es una caché: sin persistencia, perderla no rompe nada (ADR-0006).
  ingress {
    external_enabled = false
    transport        = "tcp"
    target_port      = 6379
    exposed_port     = 6379

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "valkey"
      image  = "docker.io/valkey/valkey:8"
      cpu    = 0.25
      memory = "0.5Gi"
      # Sin "--save": Azure convierte el argumento vacío en null. Las copias que guarde Valkey quedan en el disco del
      # contenedor, que se pierde al reiniciar, así que igual no persiste.
      args = ["valkey-server", "--appendonly", "no", "--maxmemory", "200mb", "--maxmemory-policy", "allkeys-lru"]
    }
  }
}

# RabbitMQ con la imagen oficial, sólo dentro del entorno. Reemplaza al plan gratuito de CloudAMQP, que admite 150
# colas por instancia: cada suscripción declara su cola y tres colas de espera para los reintentos (ADR-0003), y el
# sistema pasa ese límite. Sin disco persistente: si el contenedor se reinicia, se pierden los mensajes que estén en las
# colas en ese momento; los eventos todavía no publicados siguen en el outbox de PostgreSQL.
resource "azurerm_container_app" "rabbitmq" {
  name                         = local.nombre_rabbitmq
  resource_group_name          = azurerm_resource_group.this.name
  container_app_environment_id = azurerm_container_app_environment.this.id
  revision_mode                = "Single"
  workload_profile_name        = local.perfil_trabajo

  secret {
    name  = "contrasena-rabbitmq"
    value = random_password.rabbitmq.result
  }

  ingress {
    external_enabled = false
    transport        = "tcp"
    target_port      = 5672
    exposed_port     = 5672

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "rabbitmq"
      image  = "docker.io/library/rabbitmq:4"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "RABBITMQ_DEFAULT_USER"
        value = "logistica"
      }

      env {
        name        = "RABBITMQ_DEFAULT_PASS"
        secret_name = "contrasena-rabbitmq"
      }
    }
  }
}

resource "azurerm_container_app" "api" {
  name                         = local.nombre_api
  resource_group_name          = azurerm_resource_group.this.name
  container_app_environment_id = azurerm_container_app_environment.this.id
  revision_mode                = "Single"
  workload_profile_name        = local.perfil_trabajo

  dynamic "secret" {
    for_each = local.secretos_api
    content {
      name  = secret.key
      value = secret.value
    }
  }

  dynamic "registry" {
    for_each = local.usa_registro ? [1] : []
    content {
      server               = "ghcr.io"
      username             = var.ghcr_usuario
      password_secret_name = "ghcr-token"
    }
  }

  # Un solo origen (ADR-0001, addendum 5): todo el tráfico, con HTTPS y certificado de Azure, va a la API.
  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "api"
      image  = var.imagen_api
      cpu    = 0.5
      memory = "1Gi"

      dynamic "env" {
        for_each = merge(local.variables_api, local.variables_logs, local.variables_otlp)
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = merge(local.variables_secretas_comunes, local.variables_secretas_otlp, { "Demo__Contrasena" = "contrasena-demo" })
        content {
          name        = env.key
          secret_name = env.value
        }
      }

      # Las migraciones corren antes de que la API atienda: se le da tiempo antes de considerarla caída.
      startup_probe {
        transport               = "HTTP"
        port                    = 8080
        path                    = "/health"
        initial_delay           = 10
        interval_seconds        = 20
        failure_count_threshold = 10
      }
    }
  }

  depends_on = [azurerm_container_app.valkey, azurerm_container_app.rabbitmq]

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}

resource "azurerm_container_app" "worker" {
  name                         = local.nombre_worker
  resource_group_name          = azurerm_resource_group.this.name
  container_app_environment_id = azurerm_container_app_environment.this.id
  revision_mode                = "Single"
  workload_profile_name        = local.perfil_trabajo

  dynamic "secret" {
    for_each = local.secretos_worker
    content {
      name  = secret.key
      value = secret.value
    }
  }

  dynamic "registry" {
    for_each = local.usa_registro ? [1] : []
    content {
      server               = "ghcr.io"
      username             = var.ghcr_usuario
      password_secret_name = "ghcr-token"
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "worker"
      image  = var.imagen_worker
      cpu    = 0.25
      memory = "0.5Gi"

      dynamic "env" {
        for_each = merge(local.variables_worker, local.variables_logs, local.variables_otlp)
        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = merge(local.variables_secretas_comunes, local.variables_secretas_otlp)
        content {
          name        = env.key
          secret_name = env.value
        }
      }
    }
  }

  # La API crea el esquema al iniciar; el Worker arranca después.
  depends_on = [azurerm_container_app.api]

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}

# --- Observabilidad -----------------------------------------------------------------------------------------------
# Aspire Dashboard (propuesta de stack, sección 3.7; letra, sección 6.12): recibe las trazas y métricas de la API y el
# Worker y muestra el tablero técnico. Guarda la telemetría en memoria: un reinicio la vacía, y la API y el Worker la
# vuelven a llenar en segundos. Entrada pública con HTTPS: el panel pide el token (terraform output -raw
# acceso_observabilidad) y el receptor exige la clave OTLP. Con observabilidad = false no se crea.
resource "azurerm_container_app" "aspire" {
  count                        = var.observabilidad ? 1 : 0
  name                         = local.nombre_aspire
  resource_group_name          = azurerm_resource_group.this.name
  container_app_environment_id = azurerm_container_app_environment.this.id
  revision_mode                = "Single"
  workload_profile_name        = local.perfil_trabajo

  secret {
    name  = "token-panel"
    value = random_password.aspire_token.result
  }

  secret {
    name  = "clave-otlp"
    value = random_password.aspire_otlp.result
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "aspire-dashboard"
      image  = "mcr.microsoft.com/dotnet/aspire-dashboard:9.5"
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_URLS"
        value = "http://+:18888"
      }

      env {
        name  = "DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"
        value = "http://+:18890"
      }

      env {
        name  = "Dashboard__Frontend__AuthMode"
        value = "BrowserToken"
      }

      env {
        name        = "Dashboard__Frontend__BrowserToken"
        secret_name = "token-panel"
      }

      env {
        name  = "Dashboard__Otlp__AuthMode"
        value = "ApiKey"
      }

      env {
        name        = "Dashboard__Otlp__PrimaryApiKey"
        secret_name = "clave-otlp"
      }
    }

    # El proxy comparte la red del contenedor del panel (localhost) y es lo único que recibe la entrada.
    container {
      name    = "proxy"
      image   = "docker.io/library/caddy:2"
      cpu     = 0.25
      memory  = "0.5Gi"
      command = ["sh", "-c"]
      args    = ["printf '%s' \"$CADDYFILE\" > /tmp/Caddyfile && exec caddy run --config /tmp/Caddyfile --adapter caddyfile"]

      env {
        name  = "CADDYFILE"
        value = local.caddyfile
      }
    }
  }
}
