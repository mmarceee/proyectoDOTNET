# Entorno productivo con Terraform

Crea el ambiente completo en Azure, región Chile Central, con el crédito de **Azure for Students** (100 USD, sin
tarjeta) (propuesta de stack, sección 6; letra, sección 6.14):

| Recurso | Servicio | Tamaño |
| --- | --- | --- |
| API (Backoffice, Portal, seguimiento y PWA) | Container App con entrada pública y HTTPS | 0,5 vCPU y 1 GiB, 1 réplica |
| Worker | Container App sin entrada | 0,25 vCPU y 0,5 GiB, 1 réplica |
| Valkey | Container App con la imagen de Valkey, sólo dentro del entorno | 0,25 vCPU y 0,5 GiB, sin persistencia |
| RabbitMQ | Container App con la imagen de RabbitMQ, sólo dentro del entorno | 0,5 vCPU y 1 GiB, sin persistencia |
| PostgreSQL 16 | Azure Database for PostgreSQL Flexible Server | B1ms (1 vCPU, 2 GiB, 32 GB) |
| Logs de los contenedores | Log Analytics | 30 días |

**Por qué RabbitMQ no es CloudAMQP:** el plan gratuito de CloudAMQP admite 150 colas por instancia. Cada suscripción
declara su cola y tres colas de espera para los reintentos (ADR-0003), y el sistema pasa ese límite: el primer
despliegue del 05/11 no pudo declarar las colas. RabbitMQ corre entonces dentro del entorno, sin disco persistente: si
el contenedor se reinicia, se pierden los mensajes que estén en las colas en ese momento. Los eventos que todavía no se
publicaron no se pierden, porque siguen en el outbox de PostgreSQL.

Las imágenes de la API y el Worker las publica el CI en GitHub Container Registry (`ghcr.io/mmarceee/proyectodotnet`).

## Costo

Las cuatro Container Apps quedan siempre encendidas: la API ejecuta el publicador del outbox y SignalR, y el Worker consume
RabbitMQ. Por eso no pueden apagarse por falta de tráfico. El costo depende de cuánto trabajen:

| | Estimación |
| --- | --- |
| Container Apps, la mayor parte del tiempo sin tráfico | ≈ 30 a 35 USD por mes |
| Container Apps, siempre con tráfico (peor caso) | ≈ 110 USD por mes |
| PostgreSQL B1ms | Gratis 12 meses si la cuenta incluye los servicios gratuitos; si no, ≈ 13 USD por mes |
| Log Analytics | Dentro de los 5 GB gratuitos por mes |

Son estimaciones con los precios de lista de Azure (no verificadas para la suscripción de estudiante): hasta la defensa del
15/11 el gasto queda muy por debajo del crédito. **Destruir el ambiente cuando no se usa** deja de consumir crédito; se
reconstruye con un `apply`. El gasto real se ve en el portal, en *Cost Management* del grupo de recursos
`logistica-produccion`: esa es la evidencia de costo (letra, sección 6.14).

## Qué hace falta antes

Lo hace cada integrante en su cuenta; nada de esto va al repositorio.

1. **Herramientas:** Terraform 1.6 o posterior y la CLI de Azure.
   ```bash
   winget install Hashicorp.Terraform Microsoft.AzureCLI
   ```
2. **Azure:** la suscripción de Azure for Students activa. Iniciar sesión y anotar el identificador de la suscripción:
   ```bash
   az login
   az account show --query id -o tsv
   ```
3. **Servicios de Azure:** registrar una vez los proveedores que usa el ambiente:
   ```bash
   az provider register --namespace Microsoft.App --wait
   az provider register --namespace Microsoft.OperationalInsights --wait
   az provider register --namespace Microsoft.DBforPostgreSQL --wait
   ```
   Con una cuenta personal (por ejemplo, un gmail), si `az login` no encuentra la suscripción, desactivar el inicio de
   sesión integrado de Windows con `az config set core.enable_broker_on_windows=false` y volver a iniciar sesión.
4. **Imágenes:** un push a `main` que pase el CI publica `api:latest` y `worker:latest` en GitHub Container Registry. La
   primera vez, en GitHub → *Packages*, cambiar la visibilidad de los dos paquetes a **Public** (*Package settings →
   Change visibility*). Si quedan privados, completar `ghcr_usuario` y `ghcr_token` (un token con `read:packages`).
5. **Correo (opcional):** las credenciales SMTP de Brevo y un remitente verificado. Sin eso, los correos fallan y quedan en
   mensajes fallidos; el resto funciona igual.
6. Copiar `terraform.tfvars.example` como `produccion.tfvars` y completarlo. Ese archivo está excluido de Git.

## Crear, actualizar y destruir

Desde esta carpeta:

```bash
terraform init
terraform plan -var-file=produccion.tfvars
terraform apply -var-file=produccion.tfvars
terraform destroy -var-file=produccion.tfvars
```

- `apply` tarda entre 10 y 20 minutos; lo más lento es PostgreSQL.
- Al iniciar, la API aplica las migraciones y carga los datos de demostración (los dos operadores, Tienda Demo y sus
  usuarios). La contraseña de esos usuarios la genera Terraform: `terraform output -raw contrasena_demo`.
- La dirección pública sale de `terraform output url`.
- `destroy` borra todo, también los datos. Reconstruir el ambiente es volver a ejecutar `apply` (escenario 7 de la defensa).
- Si Azure responde `RequestDisallowedByAzure`, la suscripción de estudiante no permite esa región. Las permitidas se
  ven con `az policy assignment list --query "[].parameters.listOfAllowedLocations.value"`; el 05/11 eran Chile
  Central, México Central, Canadá Central, North Central US y Sudáfrica Norte.
- **Cambiar sólo un secreto no reinicia las aplicaciones:** Container Apps crea una revisión nueva cuando cambia la
  plantilla (imagen o variables), no cuando cambia un secreto, y la revisión en curso conserva el valor anterior. Después
  de un `apply` que sólo cambia secretos, reiniciar la revisión activa:
  ```bash
  az containerapp revision restart -g logistica-produccion -n logistica-produccion-api --revision <revisión>
  ```
  (`az containerapp show -g logistica-produccion -n logistica-produccion-api --query properties.latestRevisionName`).
- En PowerShell, el argumento va entre comillas: `terraform apply "-var-file=produccion.tfvars"`. Sin ellas, PowerShell
  lo parte en dos en el punto y Terraform responde *Too many command line arguments*.

Otro ambiente (por ejemplo, de prueba) es otro archivo de variables con otro `entorno`: los nombres de los recursos
cambian y no se pisan. Cada ambiente necesita su propio estado (otra carpeta, o `terraform workspace new prueba`).

## Despliegue continuo

En cada push a `main`, el CI (`.github/workflows/ci.yml`):

1. compila y publica las imágenes con la etiqueta del commit (job `docker-images`);
2. si pasan las pruebas, las promueve a `latest` y actualiza la imagen de las dos Container Apps (job `deploy`).

El paso 2 necesita el secreto `AZURE_CREDENTIALS` en el repositorio (*Settings → Secrets and variables → Actions*), con
una entidad de servicio limitada al grupo de recursos:

```bash
az ad sp create-for-rbac --name logistica-ci --role contributor --scopes /subscriptions/<suscripción>/resourceGroups/logistica-produccion --json-auth
```

Se pega la salida JSON completa como valor del secreto. Sin el secreto, o con el ambiente destruido, el CI sólo promueve
las imágenes. Si la cuenta de estudiante no permite crear la entidad de servicio, se despliega a mano con la misma orden
que usa el CI:

```bash
az containerapp update -g logistica-produccion -n logistica-produccion-api --image ghcr.io/mmarceee/proyectodotnet/api:<commit>
```

Terraform no vuelve a tocar la imagen de las Container Apps (`ignore_changes`): la maneja el despliegue.

## Estado y secretos

- El estado (`terraform.tfstate`) queda **local**, en la máquina de quien ejecuta `apply`, y fuera de Git: contiene las
  contraseñas y las claves generadas. Conviene guardar una copia en un lugar seguro, porque sin él hay que borrar los
  recursos desde el portal (borrar el grupo de recursos alcanza).
- Terraform genera la contraseña de PostgreSQL, las claves del seguimiento público (CU-60) y de cifrado de los avisos
  (CU-63) y la contraseña de demostración. Van a Container Apps como secretos.
- PostgreSQL acepta conexiones de los servicios de Azure y de la IP de `ip_administracion`; Valkey sólo es accesible desde
  dentro del entorno de Container Apps.

## Qué cambia en producción

- `ASPNETCORE_ENVIRONMENT=Production`: la API y el Worker no arrancan sin PostgreSQL, Valkey ni RabbitMQ
  (`ConexionesObligatorias`), así la caché no cae a la memoria del proceso por un error de configuración. Tampoco arranca
  la API sin `Demo__Contrasena`.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`: Container Apps termina HTTPS y reenvía por HTTP; la API toma el esquema y la
  IP del cliente de los encabezados `X-Forwarded-*`.
- Las claves de Data Protection, que cifran la cookie de sesión, se guardan en PostgreSQL (`ClavesProteccionDbContext`):
  un nuevo despliegue no cierra las sesiones ni invalida las invitaciones pendientes.
- PostgreSQL exige TLS (`SSL Mode=Require`); el pool queda en 12 conexiones para la API y 6 para el Worker.
- Una sola réplica de la API: el equipo de tres está eximido de las dos instancias balanceadas.
- Sin observabilidad en producción: el tablero de Aspire se muestra en el entorno local. La instrumentación sigue en el
  código y se activa con `OTEL_EXPORTER_OTLP_ENDPOINT`.

## Historia

El primer diseño (03/11/2026) usaba DigitalOcean App Platform con el crédito de estudiante de GitHub. DigitalOcean retiró
ese beneficio el 01/08/2026, así que el 05/11/2026 el ambiente pasó a Azure for Students, con la misma forma: dos
componentes administrados con HTTPS, PostgreSQL administrado y Valkey. São Paulo no está entre las regiones permitidas
de la suscripción de estudiante, así que quedó en Chile Central. RabbitMQ empezó en CloudAMQP y pasó el mismo día a una
Container App por el límite de colas del plan gratuito.
