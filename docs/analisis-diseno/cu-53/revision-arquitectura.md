# CU-53 · Revisión contra documentación y ADR

Revisión del 21/10/2026 solicitada por Lucas. Se contrastaron los ADR-0001 a ADR-0006, CU-53, el modelo de dominio y los acuerdos de este chat con el código local. No se modificaron los ADR para justificar la implementación.

## Resultado

Los límites estructurales del ADR-0001 se cumplen y pasan las 56 pruebas de arquitectura. Se corrigieron desvíos de la primera implementación que esas pruebas no cubrían. No se certifica cumplimiento completo de ADR-0004: quedan los pendientes de CU-52 enumerados abajo y falta ejecutar las pruebas de PostgreSQL.

| Regla documental | Evidencia y resultado |
| --- | --- |
| ADR-0001 §2.1: Ejecución es dueño de PosicionVehiculo | Entidad, DbContext y migración en Ejecución. Otros módulos reciben resultados inmutables. Cumple estructuralmente. |
| ADR-0001 §2.2–2.4: reglas en Domain; Application coordina; infraestructura implementa puertos | Reglas de coordenadas en PosicionVehiculo y de jornada en JornadaSeguimiento; handler en Application/Features/ReportarPosicion; repositorio y EF en Infrastructure. Se corrigió la regla temporal que estaba en el handler. |
| ADR-0001 §2.4: sólo Contracts entre módulos, sin HTTP interno | Consultas a Planificación y Administración por ModuleApi. La PWA sólo utiliza Http.Contracts. No se agregaron referencias al interior de otro módulo. |
| ADR-0001 §2.5 y ADR-0003 §1: reacciones recuperables en memoria | PosicionReportada después del commit; un fallo de consumidor no revierte la posición. La consulta de últimas posiciones permite recuperar el tablero. Admisible porque no hay una modificación durable dependiente de ese aviso. |
| ADR-0002 §2.1–2.3: filtro e interceptor | PosicionVehiculo implementa IOperadorOwned, usa ModuleDbContext y el operador proviene del contexto de CU-52. Las pruebas reales con dos operadores están escritas, sin ejecutar por Docker. |
| ADR-0002 §2.4 y §4: mantenimiento aislado | Se quitó el SQL global de DepurarPosiciones. Se consultan IDs de operadores activos mediante Administración y se procesa cada uno en un ámbito nuevo, fijando InquilinoTrabajo antes de resolver DbContext. ExecuteDelete conserva Tenant y no cambia OperadorId. |
| ADR-0004 §2.1–2.2 y §2.6: operaciones, idempotencia y transacción por operación | ReportarPosicion reutiliza CU-52. Posición y OperacionDispositivo se guardan en la misma unidad. El reenvío responde el resultado existente y no vuelve a emitir eventos. |
| ADR-0004 §2.5 y §6: evidencia de rechazos hasta revisión | Se eliminó la limpieza del contenido GPS de rechazos vencidos. Los 30 días acordados corresponden al historial de posiciones aceptadas, no autorizan eliminar evidencia de conflictos. |
| ADR-0004 §2.5 y ADR-0003 §2.2–2.3: aviso durable de rechazo | Se agregó IIntegrationEventPublisher de Ejecución y su adaptador al Outbox. El coordinador registra ConflictoSincronizacionRegistrado.v1 una vez por rechazo nuevo, en la misma transacción y con tenant/correlación tomados por ModuleDbContext. Su consumidor pertenece a CU-66. |
| ADR-0004 §2.7: dos fechas, UTC y corrección de reloj | CapturadaEn y RecibidaEn distintas; se reutiliza la tolerancia existente de dos minutos. La última posición se ordena por captura, no por recepción. |
| ADR-0004 §2.8: reintento periódico independiente de captura | Corregido: se reintentan pendientes aunque no haya ruta capturable o se deniegue GPS; espera creciente de 30 s hasta 5 min; una promesa compartida evita envíos simultáneos dentro de la pestaña; tras aplicar se actualiza la hoja. Con Web Locks hay coordinación entre pestañas. |
| ADR-0004 §2.9: transporte, pendientes y correlación | POST existente, identidad desde la sesión, Pendientes informado en lotes GPS y traceparent reutilizado. El histograma informa muestras, no un total de teléfonos desconectados. Otras métricas exigidas siguen pendientes en CU-52. |
| ADR-0005: PWA WebAssembly y estado local | Seguimiento en el navegador, sin Blazor Server ni dependencias de módulos en el cliente. |
| ADR-0006: caché | No se añadió caché de posiciones; se consulta persistencia aislada. No se introdujeron claves o invalidaciones que alteren el ADR. |
| CU-53 y acuerdo con Lucas | Captura programada cada 30 s con PWA visible, cola cifrada sin conexión, corte al confirmar rendición y retención configurable. Capturas anteriores a rendición se conservan como historial aunque lleguen tras el cierre. |

## Pendientes que impiden afirmar cumplimiento completo

1. **ADR-0004 §2.8: límite de 5 MB por lote.** Se encontró el límite de 20 operaciones, pero no un presupuesto por bytes en el emisor ni su validación específica en el endpoint. Es una carencia de CU-52 compartida con operaciones que contienen firmas y fotos; no se corrigió dentro de esta revisión de CU-53.
2. **ADR-0004 §2.4 y §2.8: rechazos GPS visibles hasta marcar como vistos.** Se conservan cifrados y en servidor, y hay avisos generales de sincronización, pero falta una lista persistente de ReportarPosicion rechazadas y su acción local de visto. Las pantallas existentes de carga/entrega/rendición no cubren ese tipo.
3. **ADR-0004 §2.9: métricas completas de sincronización.** Hay trazas y muestras de pendientes; no se encontraron instrumentos de métricas de operaciones aplicadas, en conflicto e inválidas por motivo ni del atraso. Son métricas transversales de CU-52.
4. **Sin Web Locks:** la promesa compartida evita concurrencia dentro de la pestaña y la base del servidor garantiza idempotencia, pero no hay una exclusión local de envíos entre distintas pestañas sin esa API. La captura GPS sí tiene exclusión mediante IndexedDB.
5. **Verificación de persistencia:** 14 pruebas de integración compilan; falta su ejecución y las regresiones de CU-51/52/54 en PostgreSQL real. El motor de Docker no responde. Tampoco se aplicó la migración ni se probó el teléfono.

La ausencia de instante de inicio en Ruta es una limitación del modelo actual: el servidor verifica estado operativo y usa DespachadaEn como límite inferior de captura. No se inventó una fecha de inicio ni se modificó el modelo de Planificación dentro de esta revisión.

## Verificación ejecutada

- 232 pruebas unitarias .NET pasan: incluyen seis escenarios de jornada y contexto de mantenimiento que no admite cambiar de operador.
- 51 pruebas JavaScript pasan: incluyen reintentos sin GPS/ruta, espera creciente, refresco de hoja y un único envío entre disparadores de la misma pestaña.
- 56 pruebas de arquitectura pasan.
- Compilación de integración: correcta, sin errores ni advertencias. Las nuevas pruebas verifican depuración entre dos operadores, evidencia de rechazo conservada y Outbox idempotente cuando esté disponible PostgreSQL.
