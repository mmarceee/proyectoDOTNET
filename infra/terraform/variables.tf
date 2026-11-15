# Variables por ambiente. Los valores de cada ambiente van en un archivo .tfvars fuera de Git
# (terraform.tfvars.example muestra el formato). Las marcadas sensitive no se muestran en plan ni en apply.

variable "entorno" {
  description = "Nombre del ambiente; forma parte del nombre de cada recurso (logistica-<entorno>)."
  type        = string
  default     = "produccion"
}

variable "azure_subscription_id" {
  description = "Identificador de la suscripción de Azure for Students (az account show --query id -o tsv)."
  type        = string
}

variable "ubicacion" {
  description = "Región de Azure. La suscripción de Azure for Students sólo permite algunas (az policy assignment list --query \"[].parameters.listOfAllowedLocations.value\"); de ellas, Chile Central es la más cercana a Montevideo."
  type        = string
  default     = "chilecentral"
}

variable "ubicacion_postgres" {
  description = "Región de PostgreSQL. Vacía, la misma que el resto; algunas suscripciones de estudiante no ofrecen PostgreSQL en todas las regiones."
  type        = string
  default     = ""
}

variable "imagen_api" {
  description = "Imagen de la API en GitHub Container Registry. El CI publica latest y la etiqueta de cada commit de main."
  type        = string
  default     = "ghcr.io/mmarceee/proyectodotnet/api:latest"
}

variable "imagen_worker" {
  description = "Imagen del Worker en GitHub Container Registry."
  type        = string
  default     = "ghcr.io/mmarceee/proyectodotnet/worker:latest"
}

variable "ghcr_usuario" {
  description = "Usuario de GitHub para descargar las imágenes, si los paquetes de GHCR son privados. Vacío si son públicos."
  type        = string
  default     = ""
}

variable "ghcr_token" {
  description = "Token de GitHub con read:packages, si los paquetes de GHCR son privados. Vacío si son públicos."
  type        = string
  default     = ""
  sensitive   = true
}

variable "smtp" {
  description = "Servidor de correo, como smtp://usuario:clave@servidor-smtp:587. Sin servidor, smtp://localhost:25: los correos fallan y quedan en mensajes fallidos."
  type        = string
  default     = "smtp://localhost:25"
  sensitive   = true
}

variable "correo_remitente" {
  description = "Dirección desde la que salen los correos; algunos servidores exigen que esté verificada."
  type        = string
  default     = "avisos@logistica.local"
}

variable "ip_administracion" {
  description = "IP que además de Azure puede conectarse a PostgreSQL (por ejemplo, para psql). Vacía por defecto."
  type        = string
  default     = ""
}

variable "ci_principal_id" {
  description = "Identificador de objeto de la entidad de servicio del CI (az ad sp list --display-name logistica-ci --query \"[0].id\" -o tsv). Recibe Contributor sobre el grupo de recursos. Vacío, el CI no despliega."
  type        = string
  default     = ""
}

variable "observabilidad" {
  description = "Crea Aspire Dashboard y envía la telemetría de la API y el Worker. false lo borra cuando no hace falta mostrarlo (ahorra crédito)."
  type        = bool
  default     = true
}
