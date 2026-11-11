# PWA en Android · entorno LAN aislado

Preparado el 09/11/2026 para probar RF 32 con el código actual y datos de demostración. Compose usa el proyecto `voluptasexpress-lan`, red, PostgreSQL, Valkey, RabbitMQ, API, Worker y Mailpit independientes. No reutiliza la base ni actualiza los contenedores del proyecto `voluptasexpress`.

## Direcciones de este equipo

| Recurso | Dirección |
| --- | --- |
| Descargar certificado público | http://192.168.1.7:8081/proyectoDOTNET-LAN.cer |
| Ingresar como repartidor | https://192.168.1.7:8443/repartidor/ingresar |
| Backoffice de demostración | https://192.168.1.7:8443/backoffice/ |
| Correos capturados en Mailpit | http://192.168.1.7:18025/ |

El teléfono debe estar en el mismo Wi-Fi/red que el equipo. El puerto 8080 sigue perteneciendo al entorno anterior, creado antes de RF 32; no usarlo para certificar la implementación nueva. La IP indicada se obtuvo de Ethernet; si el router la cambia, el certificado y la configuración requieren una actualización explícita. Conviene reservar esa IP en DHCP para mantener también el origen del almacenamiento de la PWA.

## Android

1. Desde Chrome, descargar `proyectoDOTNET-LAN.cer` con el primer enlace.
2. En Ajustes buscar **Instalar un certificado**. En muchos equipos está en Seguridad → Más ajustes de seguridad → Cifrado y credenciales. Elegir **Certificado de CA**, no certificado de Wi-Fi o de cliente/VPN. Seleccionar el archivo descargado y confirmar con el PIN del teléfono si lo solicita. Los nombres varían según fabricante/versión.
3. Abrir la dirección HTTPS de repartidor. Debe entrar sin error de certificado; no usar «continuar de todos modos» como prueba de confianza HTTPS.
4. Ingresar con `repartidor@operador-demo.uy` y contraseña **`Demo-2026!`**. Para preparar los envíos/rutas, usar en Backoffice `admin@operador-demo.uy`, `despacho@operador-demo.uy` y `deposito@operador-demo.uy`, con la misma contraseña de demostración. Comercio: `comercio@tiendademo.uy`.
5. Permitir cámara y ubicación cuando la PWA las solicite. Primero descargar la hoja con conexión e instalar el shell; después seguir la [guía de aceptación física](../../docs/analisis-diseno/rf-32/verificacion-cierre.md#aceptación-física-que-requiere-dispositivo).

Los datos iniciales crean operadores, usuarios y recursos de demostración; este entorno nuevo no contiene los envíos/rutas de la base anterior. Antes de escanear, preparar un envío de tres bultos con una versión de reglas que admita parcial, recibirlo y despachar su ruta al repartidor demo. Mailpit captura los correos sin enviarlos a cuentas externas. El cierre diario está desactivado para esta prueba.

### Escenario preparado para el ensayo del 09/11

Lucas confirmó descarga del certificado después de aplicar la regla de firewall, instalación en Android e ingreso correcto por HTTPS. Se preparó mediante las pantallas/contratos normales de la aplicación, con sesiones independientes por perfil, el siguiente escenario:

- Envío `ENV-000001`, destinatario ficticio `RF32 ANDROID · Prueba`.
- Tres bultos recibidos en depósito: `ENV-000001-1`, `ENV-000001-2`, `ENV-000001-3`.
- Nueva versión publicada: parcial habilitada; firma, foto, nombre y documento del receptor requeridos; pendientes con nueva visita sin recargo.
- Ruta `01a1c2ef-36d7-7d71-870b-49f32b836733`, del 09/11/2026, despachada al repartidor demo.
- Carga: `https://192.168.1.7:8443/repartidor/carga?rutaId=01a1c2ef-36d7-7d71-870b-49f32b836733`.
- Etiquetas descargadas desde el generador de la aplicación: `TestResults/lan/etiquetas-rf32.pdf`. Abrir en la PC para escanearlas con Android.

La carga y la visita permanecen sin registrar para realizarlas físicamente desde el teléfono. El manifiesto de la preparación queda en `TestResults/lan/escenario-rf32.json`; `scripts/preparar-escenario-rf32.cjs` conserva los pasos creados para reanudar sin volver a dar de alta el envío. No borra datos ni fuerza estados por SQL. Si el escenario pertenece a otra fecha operativa, se detiene para revisar la ruta.

### Continuación del ensayo del 10/11

El 10/11 se creó `ENV-000002` con los bultos `ENV-000002-1`, `ENV-000002-2` y `ENV-000002-3`, recibidos en depósito y con las reglas de parcial ya publicadas. La primera planificación detectó correctamente que el despacho del día anterior todavía reservaba al repartidor y al vehículo.

Lucas actualizó Mis rutas en Android y confirmó **Pendiente de resolución por despacho** para la ruta del 09/11. El servidor también mostraba esa ruta Despachada y sus tres bultos AsignadoARuta. Con esa comprobación se anuló el despacho por la operación normal de CU-43, conservando la ruta anterior Planificada y su historial; no se forzaron estados ni se borraron operaciones.

La ruta del 10/11 es `01a1c6c7-7441-72ce-a79d-c952a2d8e8c9`, despachada al mismo repartidor demo. Carga: `https://192.168.1.7:8443/repartidor/carga?rutaId=01a1c6c7-7441-72ce-a79d-c952a2d8e8c9`. Etiquetas: `TestResults/lan/etiquetas-rf32-2026-11-10.pdf`. El manifiesto del 09/11 se conserva en `TestResults/lan/escenario-rf32-2026-11-09.json`; el actual permanece en `escenario-rf32.json`.

Para preparar otro día, `node scripts/preparar-escenario-rf32.cjs --nuevo-dia` archiva el manifiesto anterior y crea otro escenario. No anula ni cierra rutas anteriores automáticamente; si hay reservas o actividad pendiente, se deben revisar antes. El escenario del 10/11 completó escaneo físico, carga/inicio, visita offline, reapertura, sincronización y declaración desde Android. Se confirmó después la recepción simulada del tercer bulto desde depósito: ruta Finalizada, sin reserva activa y dos bultos entregados. Resultados y límites en el [registro del ensayo](../../docs/analisis-diseno/rf-32/ensayo-android-2026-11-10.md).

La CA se llama **proyectoDOTNET LAN Test CA**. Huella SHA-256 del certificado preparado en este equipo:

```text
DCAD39D859EA9BC8C7CFF25F85DE00AA90625C6ADFCEAE36BA45E8E7579917B9
```

Sólo se descarga el certificado público. La clave privada del servidor y las contraseñas permanecen en `TestResults/lan`, ignorado por Git; la clave privada de la CA no se conserva después de emitir el certificado del servidor. No se instaló esta CA en Windows. Al terminar se puede quitar únicamente esta CA desde las credenciales de usuario de Android.

## Preparación y reinicio

### Arranque diario y defensa

Con Docker Desktop abierto, ejecutar desde la raíz, tanto en Windows PowerShell como en CMD o PowerShell 7:

```powershell
.\iniciar-demo.cmd
```

El lanzador usa directamente Docker Compose y la IP guardada en `TestResults/lan/ambiente.env`. Inicia las imágenes ya compiladas en segundo plano, conserva los datos y muestra la URL. No ejecuta scripts PowerShell, no requiere PowerShell 7 y no modifica la política de ejecución de Windows. También se puede ejecutar desde otra carpeta usando la ruta completa del `.cmd`.

El entorno debe estar preparado previamente. Este arranque no actualiza las imágenes con cambios del repositorio, no genera otro certificado y no crea una ruta para la fecha del día. Para actualizar las imágenes o preparar el entorno por primera vez se usa el procedimiento siguiente.

### Preparación inicial o actualización de imágenes

Requiere Windows, PowerShell 7, Node.js y Docker Desktop. Desde la raíz:

```powershell
./scripts/preparar-lan.ps1 -Ip 192.168.1.7
```

El script genera certificados/configuración sólo en el primer uso, prepara una copia de las fuentes sin `bin`/`obj`, compila las imágenes y levanta el proyecto independiente. Las credenciales y claves son propias de ese entorno; no se leen los secretos del `.env` principal. El certificado del servidor dura 90 días. Si cambia la IP o vence, el script se detiene y conserva configuración/datos; no borrar `ambiente.env` para intentar regenerarlo porque contiene las credenciales de los volúmenes existentes.

Para preparar archivos sin arrancar, agregar `-SoloPreparar`. Para iniciar imágenes ya compiladas sin volver a compilarlas:

```powershell
./scripts/preparar-lan.ps1 -Ip 192.168.1.7 -SinCompilar
```

El servidor de descarga del certificado se inició como proceso Node oculto. Si se reinicia Windows, ejecutarlo en una terminal aparte:

```powershell
node scripts/servidor-certificado-lan.cjs TestResults/lan/public/proyectoDOTNET-LAN.cer 192.168.1.7
```

Entrega sólo el certificado público y su página de instrucciones, nunca el PFX ni un listado de directorios. No es necesario mantener este servidor de descarga una vez instalado el certificado.

Para detener el entorno sin borrar los datos:

```powershell
docker compose --env-file TestResults/lan/ambiente.env -f infra/lan/compose.yml stop
```

No agregar `-v` ni eliminar volúmenes como parte de la prueba. Para consultar su estado:

```powershell
docker compose --env-file TestResults/lan/ambiente.env -f infra/lan/compose.yml ps
```

## Verificación realizada y diagnóstico

El 10/11 se completó además la [aceptación integrada de RF 32](../../docs/analisis-diseno/rf-32/aceptacion-integrada-2026-11-10.md), con ENV-000003 para conflicto y ENV-000004 para entrega parcial, Backoffice/público y receptor HTTPS de avisos v1/v2. Las dos suscripciones temporales quedaron desactivadas y se detuvieron receptor y túnel después de guardar el resultado. La ruta de ese segundo ensayo permanece EnCurso, pendiente de declaración/rendición del repartidor; no se libera automáticamente por cerrar la verificación.

Se compilaron API y Worker desde las fuentes actuales. Se verificaron `/health`, login con antiforgery, sesión real de repartidor, shell publicado, service worker de producción y módulo de evidencia con el callback nuevo. La comprobación Node usó la CA pública con validación de nombre y cadena TLS, sin desactivar errores de certificado. Resultado en `TestResults/lan/verificacion.log`. La salud del entorno anterior en el puerto 8080 siguió respondiendo 200.

El 10/11 se verificó además el arranque con `powershell.exe -NoProfile -ExecutionPolicy Restricted -Command "& '.\iniciar-demo.cmd'"`: el lanzador terminó con código 0 y los servicios quedaron en ejecución. Evita el error de scripts deshabilitados sin modificar políticas del equipo; la preparación avanzada sigue requiriendo PowerShell 7. Referencia: [políticas de ejecución de PowerShell](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies).

En Windows, curl con Schannel puede exigir revocación que esta CA de ensayo no publica. Para comprobar la cadena/nombre con curl sin instalar la CA en Windows:

```powershell
curl.exe --ssl-revoke-best-effort --cacert TestResults/lan/public/root.crt https://192.168.1.7:8443/health
```

Si el teléfono no abre ni el enlace HTTP del certificado, comprobar misma red, ausencia de aislamiento entre clientes Wi-Fi y permisos del firewall para los puertos TCP 8081 y 8443. En la comprobación posterior Lucas confirmó que Android tiene IP `192.168.1.5`. Se aplicó con elevación el script `scripts/habilitar-lan-firewall.ps1`: regla `proyectoDOTNET-LAN-RF32`, sólo entrada TCP a `192.168.1.7`, puertos 8081/8443, desde `192.168.1.0/24` y únicamente con perfil de red Privado. Se verificaron sus filtros; no se desactivó el firewall. El resultado está en `TestResults/lan/firewall-result.json`.

Para retirar únicamente esa regla al terminar, ejecutar en PowerShell como administrador:

```powershell
Remove-NetFirewallRule -Name proyectoDOTNET-LAN-RF32
```

Si el certificado descarga pero HTTPS sigue mostrando error, revisar que se instaló como CA, fecha/hora del teléfono y que se está usando exactamente la IP del certificado. Lucas confirmó acceso y completó el circuito principal el 10/11; los escenarios adicionales pendientes constan en el registro del ensayo.

Referencias: [certificados en Android](https://support.google.com/pixelphone/answer/2844832?hl=es), [endpoints HTTPS de Kestrel](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0), [CertificateRequest](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterequest?view=net-10.0).
