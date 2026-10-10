# Revisión de organización y arquitectura del CU-40

Fecha: 09/10/2026. Referencia: [ADR-0001](../ADR-0001-estilo-arquitectonico.md), secciones 2.2, 2.3 y addenda; [ADR-0002](../ADR-0002-estrategia-de-multitenancy.md) para el aislamiento.

Se revisaron los archivos incorporados por el CU-40 en el commit `1799495`, sus ajustes posteriores de interfaz e integración del tenant y los archivos auxiliares afectados por esta reorganización. Esta revisión trata ubicación, responsabilidades, visibilidad y dependencias; no cierra los pendientes funcionales de otros casos de uso.

## Desvíos encontrados y corregidos

| Antes | Después | Motivo |
| :--- | :--- | :--- |
| `Planificacion/Application/Rutas/PlanificacionService.cs`, con `Application/Features` vacío | Handlers en `Application/Features/ArmarRuta`, `ConsultarRutas`, `ConsultarEnviosDisponibles` y `ConsultarRecursosPlanificacion` | El ADR exige casos de uso bajo Features. Se separaron las consultas de la coordinación de creación, agregado, modificación y prevalidación del CU-40. |
| Todos los endpoints y páginas de Planificación en `Presentation/Features/Rutas` | Endpoints en las mismas cuatro funcionalidades; nueva ruta, detalle y parciales en `ArmarRuta`; listado en `ConsultarRutas` | Cada funcionalidad reúne su handler y presentación. Las rutas HTTP explícitas conservan sus direcciones. |
| Requests HTTP usados como entrada del servicio | Commands internos en `Application/Features/ArmarRuta` | Los endpoints convierten los requests al comando correspondiente. |
| `AdministracionModuleApi` y `DepositoModuleApi` en `Infrastructure/Persistence` | Implementaciones internas en `Application` | El ADR ubica allí la implementación de la interfaz intermodular. EF y SQL quedan en `RecursosPlanificacionReader` y `RecepcionRepository`, detrás de interfaces. |
| Records de Administración junto a su interfaz; medidas de Depósito en la raíz de Contracts | Un archivo por record en `Contracts/Results` | Contracts expone interfaces y datos inmutables, sin entidades de dominio. |
| Entidades, configuraciones y DTO agrupados en `ConfiguracionPlanificacion`, `DatosPlanificacion`, `RutaConfigurations`, `PlanificacionConfigurations` y `RutaContracts` | Archivos con el nombre de cada tipo, en sus capas originales | Facilita localizar las clases y coincide con la organización usada en Envíos. Separar estos archivos es una mejora de orden; el agrupamiento por sí solo no violaba las dependencias entre capas. |
| `ResponsablePlanificacion` junto al filtro de acceso | Archivo propio en Presentation | Lee claims de ASP.NET Core para implementar una interfaz de Application. |

## Estructura de Planificación

```text
Logistica.Modules.Planificacion/
  Application/
    Features/
      ArmarRuta/                         # handler y commands del CU-40
      ConsultarRutas/                    # listado, detalle e historial
      ConsultarEnviosDisponibles/        # candidatos y filtros
      ConsultarRecursosPlanificacion/    # recursos y fecha del operador
    Abstractions/                       # confirmación y responsable
    Exceptions/                         # conflictos y restricciones
    Mapping/                            # resultados comunes de ruta
    Services/                           # preparación común de carga y franjas
  Domain/Rutas/                         # entidades, estados e invariantes
  Infrastructure/Persistence/
    Configurations/
    Migrations/
    PlanificacionDbContext.cs
    RutaRepository.cs
    ConfirmacionPlanificacion.cs
  Presentation/
    Features/
      ArmarRuta/                         # endpoint, nueva, detalle y parciales
      ConsultarRutas/                    # endpoint y listado
      ConsultarEnviosDisponibles/        # endpoint
      ConsultarRecursosPlanificacion/    # endpoint
    AccesoPlanificacion.cs
    ResponsablePlanificacion.cs
    PlanificacionEndpointFilters.cs
    _ViewImports.cshtml
  PlanificacionModule.cs                # entrada pública y registros
```

`Features` contiene funcionalidades de aplicación; `Domain/Rutas` puede seguir llamándose Rutas porque agrupa conceptos del dominio. Los colaboradores comunes en Abstractions, Exceptions, Mapping y Services no son casos de uso independientes. No hace falta crear features vacías para ellos.

## Resto de archivos del CU-40

| Área y archivos revisados | Ubicación y resultado |
| :--- | :--- |
| Administración: Repartidor, Vehiculo, Zona, FranjaHoraria y VersionReglasPlanificacion | `Domain/Planificacion`: propietario de recursos y configuración según el ADR. Se conservaron las reglas de vigencia allí. |
| Administración: DbContext, seed DatosIniciales, configuraciones y migraciones | `Infrastructure/Persistence`: EF, seed y SQL permanecen allí. La API de Application usa el reader de recursos; no conoce EF. |
| Depósito: RecepcionDeposito e IRecepcionRepository | `Domain/Recepciones`: el repositorio expone lecturas de recepciones internas. Application transforma sus medidas al contrato público. |
| Depósito: repositorio, DbContext, configuraciones y migraciones | `Infrastructure/Persistence`: consultas protegidas por el filtro global de tenant. |
| Envíos: Envio e IEnvioRepository | `Domain/Envios`: conserva la propiedad de estados y compromisos del envío. Planificación sólo invoca Contracts. |
| Envíos: EnviosModuleApi e IEnviosModuleApi | Implementación interna en Application y contrato público en el proyecto Contracts: ya estaban correctamente ubicados. |
| Envíos: EnvioPlanificable, BultoPlanificable y AsignacionEnvioRuta | `Contracts/Results`, ahora separados en archivos propios. No se expone Envio ni Bulto. |
| Envíos: repositorio, DbContext y migraciones | `Infrastructure/Persistence`: consulta y persistencia, incluida la escritura técnica de Outbox. |
| Planificación: Ruta, Parada, ValidacionRuta, BultoValidacionRuta y PlanificadorDeRuta | `Domain/Rutas`: sin referencias a EF ni ASP.NET Core. Reglas de capacidad y franjas permanecen en dominio. |
| Planificación: repositorio, DbContext, configuraciones, confirmación transaccional y migraciones | `Infrastructure/Persistence`: conserva RF 15, reservas, transacción y traducción de conflictos de persistencia. |
| PlanificacionModule, AdministracionModule y DepositoModule | Raíz de cada módulo: composición y registro de implementaciones internas. Se actualizaron los registros por los nuevos handlers y readers. |
| DTO de rutas | `Logistica.Http.Contracts/Planificacion`: proyecto sin dependencias, separado de los contratos intermodulares. |
| Acceso, responsable, filtros HTTP, PageModels y parciales | Presentation: ASP.NET Core, validación de formulario y comportamiento de interfaz permanecen en esta capa. Los modelos invocan handlers. |
| ModuleDbContext y OutboxMessage | `BuildingBlocks.Infrastructure/Persistence`: infraestructura técnica compartida, sin conceptos de rutas ni envíos. La participación transaccional y el aislamiento se mantienen. |
| TenantProvisorio y launchSettings.json | Host `Logistica.Api`: resolución del tenant y opciones locales de ejecución. La sesión real sigue pendiente; esta revisión no la reemplaza. |
| Enlace de rutas en _Layout.cshtml | Shell de `Logistica.Backoffice`: navegación general. Las pantallas del caso permanecen en el módulo. |
| Proyectos y referencias de pruebas | `tests`: pruebas unitarias de dominio, arquitectura e integración; acceso a internos para verificar el módulo. |
| ArmarRutaTests, AislamientoPlanificacionTests, PlanificarRutaTests y FranjasPlanificacionTests | Carpetas de su módulo dentro del proyecto de pruebas correspondiente. Cubren flujo, restricciones, concurrencia, atomicidad y aislamiento. |
| scripts/cu40-en-docker.sh | `scripts`: herramienta de desarrollo, sin participación en el flujo de la aplicación. |
| casos-de-uso.md, modelo-de-dominio.md y tabla-de-transiciones-de-estados.md | Documentos compartidos de `docs/analisis-diseno`; mantienen las decisiones de negocio. |
| Documentos de trabajo e integración de CU-40 | `docs/analisis-diseno/cu-40`: carpeta temporal acordada con Lucas; consolidar y retirar al cerrar. |

## Verificación

Las pruebas existentes de arquitectura verificaban dependencias y visibilidad, pero no detectaban un handler fuera de Features ni una API intermodular implementada en Infrastructure. Se agregaron doce verificaciones, una por módulo para cada regla, en `ModuleOrganizationTests`.

La compilación y las pruebas se ejecutan mediante `scripts/cu40-en-docker.sh test`, en una copia aislada del checkout. Se conserva la base de datos y el proceso local de desarrollo. Las URLs, tablas, migraciones y reglas del CU-40 no se modificaron por esta reorganización.

Resultado: compilación Release sin errores ni advertencias y **170 pruebas aprobadas**: 40 unitarias, 54 de arquitectura y 76 de integración. No hubo pruebas fallidas ni omitidas. Las pruebas de integración comprueban también las páginas Razor, sus handlers y los escenarios de concurrencia, atomicidad y aislamiento.

El cierre del caso sigue sujeto a los pendientes registrados en [avance-cu-40.md](avance-cu-40.md), especialmente sesión real y comprobaciones de integración.
