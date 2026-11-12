# RF 34 · Despliegue local y LAN

Fecha: 10/11/2026, America/Montevideo. Solicitado por Lucas para ambos ambientes. Base de código: `04ee4a4` más el árbol de trabajo de RF 34, sin commit de estos cambios.

## Actualización realizada

Se detuvieron las API y Worker de ambos ambientes y la API nativa local. Se respaldaron ambas bases con `pg_dump` en formato custom y se restauraron copias independientes para ensayar la actualización. Se aplicaron primero Envíos, luego Planificación y finalmente Depósito, tanto en las copias como en las bases originales:

1. `20261110210453_CustodiaBultosRF34`.
2. `20261110210531_ReservasBultosRF34`.
3. `20261111005549_IdempotenciaCustodiaRF34`.

Los scripts incrementales parten de la última migración realmente aplicada de cada módulo. Los respaldos previos se conservan en `TestResults/rf34/deploy/backups/local-antes-rf34.dump` y `lan-antes-rf34.dump`, ignorados por Git. Los logs, scripts, versiones previas y controles están en `TestResults/rf34/deploy/`; no contienen las credenciales de conexión.

Antes de reabrir escrituras se verificaron las tres versiones, ausencia de reservas abiertas duplicadas, pertenencia de reservas a bultos/envíos/rutas del operador correcto y consistencia de movimientos y confirmaciones de custodia. Se conservaron los conteos previos:

| Base | Envíos | Bultos | Operaciones de dispositivo | Reservas abiertas reconstruidas | Pendientes con ubicación desconocida |
| --- | ---: | ---: | ---: | ---: | ---: |
| Local (`VoluptasExpress`) | 31 | 41 | 214 | 7 | 31 |
| LAN (`logistica_lan`) | 4 | 12 | 38 | 4 | 4 |

Los 31/4 bultos con ubicación desconocida siguen bloqueados para nueva planificación hasta confirmar su presencia física desde `/backoffice/custodia`. El cierre administrativo o su estado operativo no sustituyen esa confirmación. Los datos migrados no se modificaron para simularla.

## Build y servicios

Se reconstruyeron e iniciaron API y Worker mediante Docker Compose en ambos ambientes. También se publicó Release de la API nativa en `TestResults/rf34/publicacion` y se inició en segundo plano con los puertos habituales. Los tres builds terminaron correctamente. El `dotnet watch` anterior quedó detenido; la API nativa ejecuta ahora la publicación nueva.

Comprobaciones posteriores al arranque:

| Servicio | Resultado |
| --- | --- |
| Docker local, `http://localhost:8080/health` | HTTP 200 |
| Nativo, `http://localhost:5221/health` | HTTP 200 |
| Nativo, `https://localhost:7279/health` | HTTP 200 |
| LAN, `https://192.168.1.7:8443/health` | HTTP 200 |
| Manifiesto PWA `/repartidor/service-worker-assets.js`, Docker local y LAN | HTTP 200 |
| API y Worker Docker, local y LAN | Los cuatro contenedores en ejecución |

`TestResults/` se excluyó del contexto Docker para mantener respaldos, evidencia y configuración privada LAN fuera de la imagen. La cola offline de la PWA no se borró ni se reescribió.

## Aceptación pendiente

Este registro acredita la actualización y el arranque; la prueba funcional manual publicada sigue pendiente. Para realizarla, usar los recorridos A/B/C de [verificacion.md](verificacion.md), con sesiones de Despacho, Depósito y Repartidor. Cerrar y volver a abrir la PWA permite tomar la actualización; conservar sus datos y operaciones pendientes.
