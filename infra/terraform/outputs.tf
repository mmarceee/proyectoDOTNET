output "url" {
  description = "Dirección pública de la aplicación (HTTPS con certificado de Azure)."
  value       = "https://${azurerm_container_app.api.ingress[0].fqdn}"
}

output "grupo_de_recursos" {
  description = "Grupo de recursos del ambiente; el job deploy del CI lo usa para actualizar las imágenes."
  value       = azurerm_resource_group.this.name
}

output "aplicaciones" {
  description = "Nombres de las Container Apps de la API y el Worker."
  value       = { api = azurerm_container_app.api.name, worker = azurerm_container_app.worker.name }
}

output "contrasena_demo" {
  description = "Contraseña de los usuarios de demostración. Se ve con: terraform output -raw contrasena_demo"
  value       = random_password.demo.result
  sensitive   = true
}

output "url_observabilidad" {
  description = "Dirección de Aspire Dashboard, o null si observabilidad = false."
  value       = var.observabilidad ? local.url_aspire : null
}

output "acceso_observabilidad" {
  description = "Enlace que inicia sesión en Aspire Dashboard con el token. Se ve con: terraform output -raw acceso_observabilidad"
  value       = var.observabilidad ? "${local.url_aspire}/login?t=${random_password.aspire_token.result}" : null
  sensitive   = true
}
