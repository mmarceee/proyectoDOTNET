# Revisión de CU-43 y su arquitectura

Fecha: 14/10/2026. Alcance: cambios locales de despacho; no incluye implementar CU-50, Identity ni el consumidor de avisos.

## Resultado

El despacho y el registro durable de su evento respetan las reglas revisadas. Se corrigió una desviación de organización: CU-43 estaba dentro del handler y endpoint de CU-40. Ahora tiene command y handler en `Application/Features/DespacharRuta`, endpoint y parcial en `Presentation/Features/DespacharRuta`. El detalle existente del Backoffice invoca el handler, igual que la integración de CU-41. CU-40 y CU-43 comparten `Application/Services/EvaluadorRuta`, sin duplicar las restricciones del dominio.

No se considera completo el flujo hasta la notificación en la PWA: faltan publicación en RabbitMQ, consumidor del Worker, persistencia y presentación del aviso. Identity de main está integrado para CU-40, CU-41 y CU-43; el login del repartidor en la PWA queda fuera de esta entrega. La suite de arquitectura está validada tras corregir las dependencias; véase abajo.

## Correspondencia con la documentación

| Requisito y referencia | Evidencia en la implementación |
| --- | --- |
| [CU-43](../casos-de-uso.md): validar antes de confirmar, rechazar ruta vacía y conservar Planificada ante fallos | Prevalidación sin escrituras; confirmación que repite la evaluación; opción oculta para rutas vacías; rollback probado. |
| CU-43: estado Despachada, fecha e inmutabilidad | `Ruta.Despachar`, `DespachadaEn` en respuesta/detalle y controles del agregado que impiden editar rutas no Planificadas. No se cambia el estado de los envíos a EnTransito. |
| [Modelo de dominio](../modelo-de-dominio.md), notas de Planificación: carga actual, reglas vigentes y evidencia histórica | Evaluación mediante contratos de Administración, Envíos y Depósito; `PlanificadorDeRuta` conserva las reglas de negocio. Nueva `ValidacionRuta` de operación Despacho, con versión, recursos, bultos y hora, en la misma transacción. |
| [ADR-0001](../ADR-0001-estilo-arquitectonico.md), §§2.2–2.6: propiedad, features, dependencias y contratos | Tipos internos; DTO HTTP en Http.Contracts; evento público en Planificacion.Contracts/Events; handler sin EF ni ASP.NET; presentación sin acceso a Infrastructure; adaptador Outbox en Infrastructure. No hay llamadas HTTP ni acceso a tablas de otro módulo desde Planificación. |
| [ADR-0002](../ADR-0002-estrategia-de-multitenancy.md): aislamiento y autorización | Repositorio con filtros existentes; revisión y ruta obtenidas en el contexto de la sesión; sin nuevos IgnoreQueryFilters ni consultas masivas. Identity integrado: con cookie se exige Despachador y el tenant proviene de sus claims. Pruebas de login real, responsable, 403 para administrador/comercio, 404 para rutas ajenas y antiforgery. Sin respaldo, Backoffice redirige al login y API responde 401. |
| CU-41 y control de concurrencia | Revisión esperada, token EF existente y bloqueo transaccional de Planificación. Prueba de despacho y retiro simultáneos: sólo una operación aplica. |
| [ADR-0003](../ADR-0003-outbox-y-mensajeria.md), §§2.1–2.3 | IIntegrationEventPublisher interno del módulo; adaptador que llama AgregarAlOutbox; RutaDespachada.v1 y evidencia guardados con el cambio. Inquilino y traza pertenecen al sobre y se toman de la infraestructura. Prueba de fallo después de SaveChanges y antes del commit: se revierten estado, evidencia y mensaje. |
| [ADR-0004](../ADR-0004-resolucion-de-conflictos-de-sincronizacion.md) y [ADR-0005](../ADR-0005-modo-de-renderizado-de-blazor.md) | No se agregan operaciones offline ni sincronización a CU-43; la descarga cifrada es CU-50. El aviso dentro de la PWA está acordado y documentado, pero aún no implementado. |

El orden actual numerado se conserva al despachar; esta entrega no implementa la propuesta automática ni el ajuste manual de CU-42.

## Verificación ejecutada

`dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~Planificacion`: **45 pruebas aprobadas**, incluidos los seis escenarios nuevos de CU-43 y las regresiones de CU-40/CU-41.

La primera ejecución de arquitectura tuvo **46 aprobadas y 8 fallidas**. Los ocho fallos de ModuleOrganizationTests eran `ReflectionTypeLoadException` al cargar `Microsoft.EntityFrameworkCore.Relational, Version=10.0.4.0`; no llegaban a evaluar las aserciones de organización. Las pruebas resolvían Relational 10.0.4 mientras la API resolvía 10.0.12.

La restauración inicial estuvo bloqueada con NU1903 para `Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.12, por GHSA-g27p-qf6j-c96c y GHSA-qjpx-jqm5-h2c4. Se corrigió fijando Relational 10.0.12 en Directory.Packages.props y referenciándolo desde BuildingBlocks.Infrastructure; así Npgsql no resuelve una versión anterior en los proyectos consumidores. Los tres paquetes Blazor se actualizaron a 10.0.13, versión corregida según los [avisos de Microsoft](https://github.com/dotnet/aspnetcore/security/advisories/GHSA-g27p-qf6j-c96c). La auditoría de NuGet permanece activa y la restauración de la solución pasa.

Con `dotnet test Logistica.slnx --artifacts-path artifacts/cu43-verificacion -v minimal`: **218 pruebas aprobadas: 54 de arquitectura, 46 unitarias y 118 de integración**, sin fallos ni pruebas omitidas. Se utilizó una salida separada porque la API en ejecución bloqueaba sus DLL de la salida habitual. No se detuvo la API; debe detenerse, recompilarse y arrancarse para cargar los paquetes nuevos en la salida habitual.

`git diff --check`: sin errores de espacios. La revisión del código complementa las pruebas; no sustituye la comprobación de permisos con usuarios reales ni la prueba del aviso desde Worker hasta PWA.
