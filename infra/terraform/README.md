# Entorno productivo con Terraform

Crea el ambiente completo en DigitalOcean y CloudAMQP (propuesta de stack, sección 6; letra, sección 6.14):

| Recurso | Servicio | Tamaño por defecto | Costo mensual aproximado |
| --- | --- | --- | --- |
| API (Backoffice, Portal, seguimiento y PWA) | App Platform, servicio web con HTTPS | `apps-s-1vcpu-1gb`, 1 instancia | 12 USD |
| Worker | App Platform, worker | `apps-s-1vcpu-0.5gb`, 1 instancia | 5 USD |
| PostgreSQL 17 | Managed Database | `db-s-1vcpu-1gb`, 1 nodo | 15,15 USD |
| Valkey 8 | Managed Caching | `db-s-1vcpu-1gb`, 1 nodo | 15 USD |
| RabbitMQ | CloudAMQP, plan `lemur` (compartido) | — | gratis |
| **Total** | | | **≈ 47 USD (≈ 0,065 USD por hora)** |

Precios de las páginas de DigitalOcean del 03/11/2026. DigitalOcean cobra por hora, así que destruir el ambiente cuando no se
usa deja de generar costo. El costo real se ve en el panel del proyecto `logistica-produccion` y en *Billing*.

**Crédito de cuenta nueva.** Las cuentas nuevas reciben 200 USD de crédito por 60 días (condiciones a confirmar en
*Billing* al crear la cuenta: piden tarjeta o PayPal). Este ambiente gasta unos 1,55 USD por día: unos 17 USD hasta la
defensa del 15/11 y unos 94 USD si queda encendido los 60 días, así que el crédito lo cubre. Se cobra a la tarjeta lo que
siga corriendo cuando el crédito vence o se agota. Por eso:

1. Verificar que el crédito figure en *Billing* antes del primer `apply`.
2. Configurar una alerta en *Billing → Billing alerts* (por ejemplo, 20 USD).
3. Ejecutar `terraform destroy` después de la defensa, y si se quiere también entre ensayos.

La evidencia de costo (letra, sección 6.14) es el panel del proyecto con el consumo cubierto por el crédito.

## Qué hace falta antes

Lo hace cada integrante en su cuenta; nada de esto va al repositorio.

1. Terraform 1.6 o posterior.
2. **DigitalOcean:** una cuenta y un token de API con lectura y escritura (*API → Tokens*).
3. **GitHub:** autorizar la aplicación de GitHub de DigitalOcean sobre el repositorio (*App Platform → Create App →
   GitHub*, se puede cancelar después de autorizar). Sin eso, App Platform no puede compilar desde `main`.
4. **CloudAMQP:** una cuenta y su clave de API (*API access*). El plan gratuito admite una instancia por cuenta.
5. **Brevo:** las credenciales SMTP (*SMTP & API → SMTP*) y un remitente verificado.
6. Copiar `terraform.tfvars.example` como `produccion.tfvars` y completarlo. Ese archivo está excluido de Git.

## Crear, actualizar y destruir

Desde esta carpeta:

```bash
terraform init
terraform plan -var-file=produccion.tfvars
terraform apply -var-file=produccion.tfvars
terraform destroy -var-file=produccion.tfvars
```

- `apply` tarda entre 10 y 15 minutos: las bases tardan en crearse y App Platform compila las dos imágenes.
- Al iniciar, la API aplica las migraciones y carga los datos de demostración (los dos operadores, Tienda Demo y sus
  usuarios). La contraseña de esos usuarios la genera Terraform y se ve con `terraform output -raw contrasena_demo`.
- La dirección pública sale de `terraform output url`.
- `destroy` borra todo, también los datos. Reconstruir el ambiente es volver a ejecutar `apply` (escenario 7 de la defensa).

Otro ambiente (por ejemplo, de prueba) es otro archivo de variables con otro `entorno`: los nombres de los recursos
cambian y no se pisan. Cada ambiente necesita su propio estado (otra carpeta, o `terraform workspace new prueba`).

## Despliegue continuo

App Platform no despliega con cada push. Lo pide el job `deploy` del CI (`.github/workflows/ci.yml`) cuando un push a
`main` pasa las pruebas y compila las imágenes. Necesita el secreto `DIGITALOCEAN_ACCESS_TOKEN` en el repositorio
(*Settings → Secrets and variables → Actions*). Sin el secreto, o con el ambiente destruido, el job termina sin desplegar.

## Estado y secretos

- El estado (`terraform.tfstate`) queda **local**, en la máquina de quien ejecuta `apply`, y fuera de Git: contiene las
  contraseñas de las bases y las claves generadas. Conviene guardar una copia en un lugar seguro, porque sin él hay que
  borrar los recursos desde el panel.
- Terraform genera las claves del seguimiento público (CU-60), la de cifrado de los avisos (CU-63) y la contraseña de
  demostración. Van a App Platform como variables de tipo `SECRET`.
- PostgreSQL y Valkey sólo aceptan conexiones de la aplicación y de las IP de `ips_administracion`.

## Qué cambia en producción

- `ASPNETCORE_ENVIRONMENT=Production`: la API y el Worker no arrancan sin PostgreSQL, Valkey ni RabbitMQ
  (`ConexionesObligatorias`), así la caché no cae a la memoria del proceso por un error de configuración. Tampoco arranca
  la API sin `Demo__Contrasena`.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`: App Platform termina HTTPS y reenvía por HTTP; la API toma el esquema y la
  IP del cliente de los encabezados `X-Forwarded-*` (redirecciones y límite del seguimiento público por IP).
- Las claves de Data Protection, que cifran la cookie de sesión, se guardan en PostgreSQL (`ClavesProteccionDbContext`):
  un nuevo despliegue no cierra las sesiones ni invalida las invitaciones pendientes.
- PostgreSQL exige TLS (`SSL Mode=Require`) y el pool queda en 12 conexiones para la API y 6 para el Worker, dentro de las
  22 que admite el nodo más chico.
- Sin observabilidad en producción: el tablero de Aspire se mostró en el entorno local. La instrumentación sigue en el
  código y se activa con `OTEL_EXPORTER_OTLP_ENDPOINT`.
