# **ADR-0008 · Despliegue e infraestructura como código**

| Estado | Aceptado — aprobado por el equipo el 15/11/2026. Despliegue continuo verificado el 15/11 (sección 5) |
| :---- | :---- |
| **Versión** | 1.2 |
| **Fecha** | 9 de noviembre de 2026 (la decisión rige desde el 5 de noviembre; aceptado el 15 de noviembre) |
| **Responsable** | Ezequiel Marcenal |
| **Equipo** | Equipo 1 - Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Decisión relacionada** | Despliegue en la nube, infraestructura como código y entrega continua (letra 6.14 y 8.2) |
| **Relacionado con** | ADR-0001 (sección 2: host único y Worker aparte); ADR-0003 (Outbox y RabbitMQ); ADR-0006 (Valkey); Propuesta de stack y entorno de desarrollo (secciones 6 y 7); `infra/terraform/README.md`; `.github/workflows/ci.yml` |

**Este registro define dónde y cómo corre el sistema fuera del equipo de un integrante: Azure Container Apps con el crédito de Azure for Students, PostgreSQL administrado, todo descrito en Terraform y actualizado por el CI en cada push a `main` que pasa las pruebas. El ambiente se destruye cuando no se usa y se reconstruye con un `terraform apply`.**

> La decisión se tomó y se aplicó el 05/11/2026; este registro se escribió después, con lo que mostró esa puesta en marcha. Las verificaciones que quedan están en la sección 5.

# **1\. Contexto y restricciones**

- **La letra (6.14)** exige: un proveedor de nube pública sobre servidor remoto real (sin túneles ni el equipo de un integrante); HTTPS con certificado válido; toda la infraestructura en código, con variables por ambiente, secretos fuera del código y las operaciones de planificación, aplicación y destrucción documentadas y ejecutables; un pipeline que compile, pruebe y despliegue; y control y evidencia del costo. Recomienda los niveles gratuitos y destruir el ambiente cuando no se use. El escenario 7 de la defensa es destruirlo y reconstruirlo.
- **Equipo de tres (6.15):** eximido del escalado y el balanceo de carga. Alcanza con una instancia de la API, aunque el objetivo del hito del 05/11 menciona dos.
- **Costo cero y sin tarjeta:** el equipo decidió no pagar el ambiente. El diseño del 03/11 usaba DigitalOcean App Platform con el crédito del GitHub Student Developer Pack, y DigitalOcean retiró ese beneficio el 01/08/2026.
- **Procesos que no pueden apagarse por falta de tráfico:** la API ejecuta el publicador del Outbox (ADR-0003) y SignalR; el Worker consume RabbitMQ. Descarta las plataformas que duermen la aplicación sin solicitudes.
- **Cinco componentes con estado o proceso propio:** la API (host único, ADR-0001), el Worker, PostgreSQL, Valkey (ADR-0006) y RabbitMQ (ADR-0003). Las imágenes de la API y el Worker ya existían para Docker Compose y el CI.
- **Repositorio público:** ninguna contraseña, clave ni estado de Terraform puede ir a Git. Los usuarios de demostración no pueden tener la contraseña de desarrollo.
- **Región:** la suscripción de estudiante sólo permite algunas regiones. El 05/11 eran Chile Central, México Central, Canadá Central, North Central US y Sudáfrica Norte; São Paulo no estaba.

# **2\. Decisión adoptada**

## **2.1 Plataforma: Azure Container Apps con Azure for Students**

Un grupo de recursos `logistica-<entorno>` en Chile Central, la región permitida más cercana a Montevideo:

| Componente | Servicio | Tamaño |
| :---- | :---- | :---- |
| API (Backoffice, Portal, seguimiento y PWA) | Container App con entrada pública y HTTPS | 0,5 vCPU y 1 GiB, 1 réplica |
| Worker | Container App sin entrada | 0,25 vCPU y 0,5 GiB, 1 réplica |
| Valkey | Container App con la imagen oficial, TCP sólo dentro del entorno | 0,25 vCPU y 0,5 GiB, sin persistencia |
| RabbitMQ | Container App con la imagen oficial, TCP sólo dentro del entorno | 0,5 vCPU y 1 GiB, sin persistencia |
| PostgreSQL 16 | Azure Database for PostgreSQL Flexible Server | B1ms, 32 GB, TLS obligatorio |
| Aspire Dashboard | Container App con un proxy Caddy, entrada pública con HTTPS | 0,75 vCPU y 1,5 GiB, se apaga con `observabilidad = false` |
| Logs de los contenedores | Log Analytics | 30 días |

- **Todas las Container Apps con una réplica fija** (mínimo y máximo 1): ninguna se apaga por falta de tráfico.
- **Container Apps da el HTTPS** con su certificado administrado. La API toma el esquema y la IP del cliente de los encabezados `X-Forwarded-*`.
- **Una red virtual propia para el entorno**, porque Container Apps la exige para exponer Valkey y RabbitMQ por TCP sólo hacia adentro. No tiene costo.
- **PostgreSQL acepta** conexiones de los servicios de Azure y, opcionalmente, de la IP de quien administra (`ip_administracion`).
- **Lo que el sistema ya tenía para producción** sigue igual: en `Production`, la API y el Worker no arrancan sin PostgreSQL, Valkey ni RabbitMQ (`ConexionesObligatorias`). Las claves de Data Protection se guardan en PostgreSQL, para que un despliegue nuevo no cierre las sesiones.

## **2.2 Infraestructura como código: Terraform con estado local**

- **Terraform** (`infra/terraform`, proveedores `azurerm` 4 y `random`) describe todo lo anterior. Se mantuvo del diseño del 03/11: al cambiar de DigitalOcean a Azure cambiaron el proveedor y los recursos, pero no la herramienta ni la forma de operar.
- **Variables por ambiente** en un archivo `.tfvars` fuera de Git (`terraform.tfvars.example` muestra el formato). El nombre del ambiente (`entorno`) forma parte del nombre de cada recurso: otro ambiente es otro archivo de variables y otro estado.
- **Secretos generados por Terraform:** la contraseña de PostgreSQL, las claves del seguimiento público (CU-60) y del cifrado de avisos (CU-63), el token de Aspire Dashboard y la contraseña de demostración. Van a Container Apps como secretos y no aparecen en el repositorio.
- **Estado local**, en la máquina de quien ejecuta `apply`, fuera de Git: contiene esos secretos.
- **Las operaciones** (`init`, `plan`, `apply`, `destroy`) están documentadas en el README de `infra/terraform`, con los problemas conocidos (regiones permitidas, comillas en PowerShell, secretos que no reinician la revisión).

## **2.3 Entrega continua: imágenes en GHCR y el job `deploy`**

1. En cada push a `main`, el job `docker-images` compila las imágenes de la API y el Worker y las publica en GitHub Container Registry con la etiqueta del commit.
2. Si pasan las pruebas, el job `deploy` las promueve a `latest` y actualiza las dos Container Apps con `az containerapp update`. La API va primero, porque aplica las migraciones al iniciar.
3. El job inicia sesión con una entidad de servicio (`AZURE_CREDENTIALS`) que sólo tiene *Contributor* sobre el grupo de recursos. **El permiso lo asigna Terraform** (`ci_principal_id`), así cada `apply` lo restaura.
4. **Terraform no vuelve a tocar la imagen** de las Container Apps (`ignore_changes`): la crea con `latest` y después la maneja el CI. Así un `apply` no revierte un despliegue.
5. Si el ambiente está destruido, el inicio de sesión falla porque la entidad de servicio no tiene acceso a nada. El job sigue: promueve las imágenes, deja un aviso y no despliega.

## **2.4 Ciclo de vida: destruir cuando no se usa**

El ambiente se crea para cada demostración y se destruye después (`terraform destroy`). Un `apply` lo reconstruye entero en 10 a 20 minutos, sin pasos manuales en el portal. Al iniciar, la API aplica las migraciones y carga los datos de demostración. Esto ahorra crédito y además es lo que la letra evalúa (escenario 7).

# **3\. Alternativas consideradas y motivo de su descarte**

- **DigitalOcean App Platform (diseño del 03/11):** tenía la misma forma (componentes administrados con HTTPS, PostgreSQL y Valkey administrados). Se descartó el 05/11 porque DigitalOcean retiró el crédito de estudiante y el equipo no quiso pagar.
- **Vercel y Netlify:** no ejecutan .NET como proceso permanente, ni un worker, ni SignalR.
- **Plan gratuito de Render:** no tiene workers, apaga la API sin tráfico (y con ella el publicador del Outbox) y borra la base a los 30 días.
- **Máquina virtual gratuita de Oracle Cloud con Docker Compose:** reutilizaría el `docker-compose.yml` tal cual. Se descartó porque exige tarjeta y porque habría que administrar el servidor (sistema operativo, parches, certificado, reinicios), y reconstruirlo con un comando pide además configurar la máquina, no sólo crearla.
- **CloudAMQP gratuito para RabbitMQ (elegido el 03/11):** se descartó en el primer `apply` del 05/11. Su plan gratuito admite 150 colas, y cada suscripción declara su cola y tres de espera para los reintentos (ADR-0003): el sistema pasa el límite y la API no pudo declarar las colas.
- **Caché administrada de Azure en lugar de Valkey en un contenedor:** no tiene plan gratuito. Valkey en el entorno alcanza, porque perder la caché no rompe nada (ADR-0006).
- **Región São Paulo:** no está permitida en la suscripción de estudiante (`RequestDisallowedByAzure`).
- **Aspire Dashboard sólo en el entorno local (decisión del 03/11):** se revirtió el 05/11, porque el hito pedía el tablero de observabilidad operativo en el ambiente desplegado.
- **Asignar el permiso del CI a mano, al crear la entidad de servicio:** fue el primer diseño. `destroy` borra el grupo y la asignación con él; desde el 06/11, con el ambiente destruido, el job `deploy` falló en cada push a `main` y, al reconstruir el ambiente, el grupo nuevo habría quedado sin permiso. Se corrigió el 09/11 (sección 2.3).

# **4\. Consecuencias**

**A favor**
- Se cumple la letra 6.14 sin costo para el equipo: nube pública, HTTPS administrado, infraestructura entera en Terraform, secretos fuera del repositorio y despliegue automático después de las pruebas.
- Las imágenes son las mismas que se prueban en Docker Compose y en el CI; producción no tiene un empaquetado propio.
- Reconstruir el ambiente es un comando. Se hizo varias veces el 05/11 para corregir problemas (región, CloudAMQP, observabilidad), y después se destruyó.
- El despliegue continuo funcionó: el 05/11 el job `deploy` actualizó las dos Container Apps con el commit recién probado (ejecuciones 42080927320 y 42114963220 de GitHub Actions).
- Los eventos no se pierden aunque se reinicie RabbitMQ: los que todavía no se publicaron siguen en el Outbox de PostgreSQL (ADR-0003).

**En contra / riesgos**
- **RabbitMQ y Valkey sin disco:** si su contenedor se reinicia, se pierden los mensajes que estaban en las colas en ese momento (incluidos los de espera para reintentos), la caché y las novedades del tablero en vivo que viajaban por el backplane. El Outbox no los vuelve a publicar, porque ya los marcó como publicados.
- **Una sola réplica de la API:** un reinicio o un despliegue deja el sistema sin servicio unos segundos. Es aceptable por la exención de 6.15.
- **El estado de Terraform depende de una persona:** está en la máquina del responsable. Si se pierde, hay que borrar el grupo de recursos desde el portal y volver a aplicar. Dos integrantes no pueden operar el mismo ambiente.
- **PostgreSQL abierto a los servicios de Azure:** la regla especial `0.0.0.0` acepta conexiones desde cualquier servicio de Azure, no sólo del entorno propio. La protegen la contraseña generada y TLS; una red privada para PostgreSQL costaría más configuración.
- **Costo controlado por estimación:** el README tiene estimaciones con precios de lista (unos 30 a 35 USD por mes con poco tráfico, 40 USD más con Aspire Dashboard), y el ambiente se destruye cuando no se usa. No se registra el gasto real.
- **Sin servidor de correo en producción (decisión del equipo, 15/11):** Terraform admite uno (`smtp` y `correo_remitente`), pero no se configura. Las invitaciones de usuarios y los avisos al destinatario fallan y quedan en mensajes fallidos; los usuarios de demostración ya están activos. Configurarlo después es sólo completar esas dos variables.
- **Detalles de la plataforma que hay que recordar:** cambiar sólo un secreto no reinicia la revisión activa; con el ambiente destruido, el job `deploy` queda en verde pero con un aviso, sin desplegar; la entidad de servicio usa un secreto de larga duración en GitHub.

**Qué dificulta a futuro**
- **Escalar la API a dos réplicas:** lo compartido ya está fuera del proceso: la caché y el backplane de SignalR en Valkey (ADR-0006, CU-66), las claves de sesión en PostgreSQL y el Outbox con `FOR UPDATE SKIP LOCKED` (ADR-0003). Faltaría sacar las migraciones del arranque de la API, porque dos réplicas que inician juntas las aplicarían a la vez.
- **Dar persistencia a RabbitMQ:** exige un volumen en Azure Files o un servicio administrado. CloudAMQP pago o Azure Service Bus; este último obligaría a reescribir la mensajería, que está hecha sobre `RabbitMQ.Client` (ADR-0003).
- **Cambiar de proveedor:** Terraform se conserva, pero los recursos son específicos de Azure; el cambio desde DigitalOcean reescribió `main.tf` casi entero.
- **Que opere más de un integrante:** pide llevar el estado a un backend remoto (por ejemplo, una cuenta de almacenamiento fuera del grupo que se destruye).
- **El crédito es finito:** pasada la entrega, mantener el ambiente encendido consume los 100 USD en unos meses.

# **5\. Verificaciones**

No hay decisiones abiertas ni verificaciones pendientes.

| Tema | Estado |
| :---- | :---- |
| Despliegue continuo con el ambiente nuevo | **Verificada el 15/11/2026.** Después de un `apply` con `ci_principal_id` (Terraform asignó *Contributor* a `logistica-ci` sobre el grupo), el push a `main` del commit `5e47f90` pasó las pruebas y el job `deploy` actualizó las dos Container Apps a la imagen de ese commit (revisión 2 de cada una; ejecución 43726783376 de GitHub Actions). `/health` respondió 200. |
| Aprobación del equipo | **Cerrada** el 15/11/2026. |

# **6\. Referencias**

- Letra del laboratorio: 6.12 (observabilidad), 6.14 (despliegue e infraestructura como código), 6.15 (calidad y exención del escalado), 8.2 (registros de decisión) y 9.3 (escenario 7 de la defensa).
- ADR-0001 (host único y Worker aparte), ADR-0003 (Outbox y RabbitMQ), ADR-0006 (Valkey).
- Propuesta de stack y entorno de desarrollo, secciones 6 y 7.
- `infra/terraform/README.md` (recursos, costo, operaciones, secretos y tablero de observabilidad) y `.github/workflows/ci.yml` (jobs `docker-images` y `deploy`).
- Bitácora de uso de IA, entradas 3.58, 3.61, 3.62, 3.93 y 3.94.

# **7\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 09/11/2026 | Versión inicial, escrita después de aplicar la decisión, con lo que mostraron el primer `apply` del 05/11 y la corrección del job `deploy` del 09/11. Borrador con IA a pedido del responsable. | Ezequiel Marcenal |
| 1.0 | 15/11/2026 | Aceptado por el equipo. Producción sin servidor de correo (sección 4). La sección 5 pasa a verificaciones pendientes: evidencia de costo y despliegue continuo con el permiso asignado por Terraform. | Ezequiel Marcenal |
| 1.1 | 15/11/2026 | Sección 5: despliegue continuo verificado con el ambiente nuevo (commit `5e47f90`, ejecución 43726783376). | Ezequiel Marcenal |
| 1.2 | 15/11/2026 | Se quita la captura de costo, por decisión del equipo: el costo se controla con la estimación y destruyendo el ambiente (secciones 4 y 5). | Ezequiel Marcenal |
