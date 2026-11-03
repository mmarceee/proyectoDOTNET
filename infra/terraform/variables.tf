# Variables por ambiente. Los valores de cada ambiente van en un archivo .tfvars fuera de Git
# (terraform.tfvars.example muestra el formato). Las marcadas sensitive no se muestran en plan ni en apply.

variable "entorno" {
  description = "Nombre del ambiente; forma parte del nombre de cada recurso (logistica-<entorno>)."
  type        = string
  default     = "produccion"
}

variable "digitalocean_token" {
  description = "Token de la API de DigitalOcean, con permisos de lectura y escritura."
  type        = string
  sensitive   = true
}

variable "cloudamqp_apikey" {
  description = "Clave de la API de CloudAMQP (API access del panel de la cuenta)."
  type        = string
  sensitive   = true
}

variable "repositorio" {
  description = "Repositorio de GitHub que App Platform compila, como dueño/nombre."
  type        = string
  default     = "mmarceee/proyectoDOTNET"
}

variable "rama" {
  description = "Rama que se despliega."
  type        = string
  default     = "main"
}

variable "region_app" {
  description = "Región de App Platform. No hay región en Sudamérica; Nueva York es la de menor latencia desde Montevideo."
  type        = string
  default     = "nyc"
}

variable "region_datos" {
  description = "Región de PostgreSQL y Valkey, dentro de la de App Platform."
  type        = string
  default     = "nyc3"
}

variable "region_rabbitmq" {
  description = "Región de CloudAMQP, la más cercana a Nueva York."
  type        = string
  default     = "amazon-web-services::us-east-1"
}

variable "tamano_api" {
  description = "Tamaño de la instancia de la API en App Platform."
  type        = string
  default     = "apps-s-1vcpu-1gb"
}

variable "tamano_worker" {
  description = "Tamaño de la instancia del Worker en App Platform."
  type        = string
  default     = "apps-s-1vcpu-0.5gb"
}

variable "tamano_base" {
  description = "Tamaño del nodo de PostgreSQL y del de Valkey (el más chico: 1 vCPU, 1 GB y 25 conexiones en PostgreSQL)."
  type        = string
  default     = "db-s-1vcpu-1gb"
}

variable "smtp" {
  description = "Servidor de correo, como smtp://usuario:clave@smtp-relay.brevo.com:587 (credenciales SMTP de Brevo; caracteres especiales codificados como en una URL)."
  type        = string
  sensitive   = true
}

variable "correo_remitente" {
  description = "Dirección desde la que salen los correos; tiene que estar verificada en Brevo."
  type        = string
}

variable "ips_administracion" {
  description = "Direcciones IP que además de la aplicación pueden conectarse a PostgreSQL y Valkey (por ejemplo, para psql). Vacía por defecto."
  type        = list(string)
  default     = []
}
