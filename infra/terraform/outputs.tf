output "url" {
  description = "Dirección pública de la aplicación (HTTPS con certificado de App Platform)."
  value       = digitalocean_app.logistica.live_url
}

output "app_id" {
  description = "Identificador de la aplicación en App Platform (doctl apps ...)."
  value       = digitalocean_app.logistica.id
}

output "contrasena_demo" {
  description = "Contraseña de los usuarios de demostración. Se ve con: terraform output -raw contrasena_demo"
  value       = random_password.demo.result
  sensitive   = true
}
