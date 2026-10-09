# Revisión del aislamiento de marce para CU-40

Fecha: 09/10/2026. Rama revisada: `origin/marce`, commit `ebea3bc` (Multitenancy: filtro global Tenant, interceptor de escrituras y pruebas de aislamiento). La referencia se actualizó desde origin antes de revisar. No se integraron cambios en lukovski.

## Qué está implementado

- ModuleDbContext recibe ICurrentTenant y aplica el filtro global con nombre Tenant. El personal ve las filas de su operador; una sesión de comercio agrega su alcance por ComercioId. Sin operador, las consultas no devuelven filas.
- TenantSaveChangesInterceptor protege SaveChanges síncrono y asíncrono: rechaza escrituras de otro operador/comercio y cambios de propietario; completa los marcadores vacíos en altas desde la sesión.
- InquilinoFijo permite construir contextos para pruebas y futuros procesos sin request.
- Administración, Envíos y Depósito reciben el tenant en sus contextos. Se retiraron filtros manuales de lectores/repositorios existentes de Envíos y del repositorio de recepciones.
- AislamientoTests contiene 11 pruebas sobre PostgreSQL real: separación de operadores y comercios, ausencia de tenant, recepciones, relaciones comerciales, escrituras ajenas, cambios de operador, asignación de marcadores a hijos y presencia del filtro.

Verificación de la rama exportada a un contenedor aislado: **124 pruebas aprobadas**, ninguna fallida ni omitida: 30 unitarias, 42 de arquitectura y 52 de integración. La compilación tuvo cero errores y dos advertencias MSB3277 por versiones transitivas de EF Core en los proyectos de pruebas. En lukovski esos proyectos ya tienen la referencia explícita a la versión central.

## Ajustes al integrar con CU-40

1. Adaptar PlanificacionDbContext para recibir ICurrentTenant y pasarlo a la base. En marce aún no existe este contexto ni las entidades del CU-40; sus pruebas no comprueban rutas.
2. Conservar ParticiparEnTransaccionAsync añadido por CU-40 en ModuleDbContext. Es necesario antes del bloqueo SQL de la fila global Operador.
3. Retirar las guardas temporales de los lectores nuevos de Administración, Planificación y Depósito, conservando las validaciones de negocio y el ID de sesión explícito del SQL de bloqueo.
4. Resolver EnvioRepository combinando el filtro global con ObtenerParaPlanificacionAsync y RegistrarAsignacion. La comparación de integración con git merge-tree detectó un conflicto textual allí. Una integración que resuelva el texto también necesita comprobar constructores y comportamiento.
5. **Outbox requiere atención:** nuestro OutboxMessage implementa IOperadorOwned pero no hereda Entity. En marce, la aplicación de filtros está dentro del recorrido restringido a tipos derivados de Entity. Por inspección del código, esa fila quedaría protegida por el interceptor de escrituras, pero no recibiría el filtro de lecturas. Hay que resolver coherentemente su alcance y la excepción de mensajería del ADR, y probarlo antes de certificar aislamiento del CU-40. La prueba estructural de marce detectaría el filtro faltante al incorporar nuestro tipo.
6. Ampliar las pruebas A/B a Ruta, Parada, ValidacionRuta, BultoValidacionRuta, recursos y Outbox según su alcance. Comprobar que un conflicto o escritura rechazada revierte toda la asignación y que la reserva del operador no permite afectar datos ajenos.
7. Revisar el seed para usar InquilinoFijo al cargar cada operador. El seed de marce todavía sólo carga el operador demo con el contexto registrado; nuestro seed agrega recursos y configuración a ese mismo operador.
8. Revalidar las migraciones y snapshots combinados, tanto desde base vacía como desde la versión anterior.

## Pendientes que también identifica Ezequiel

La sección 5 de docs/guia-aislamiento-por-inquilino.md en marce enumera:

- Segundo operador en el seed con contexto de tenant fijo.
- Identity y reemplazo de TenantProvisorio por los claims de la cookie (identificado como trabajo de Cristian).
- Actualización del texto de ADR-0001/0002 para reflejar la implementación.
- Prueba estructural que detecte propiedades OperadorId sin el marcador correspondiente, con las excepciones del ADR.
- Traducir TenantMismatchException a 404 genérico y registrarla en el log. El host de marce no tiene un handler específico; actualmente esa excepción llega al manejo genérico como 500.
- Regla de arquitectura que restrinja IgnoreQueryFilters, ExecuteUpdate, ExecuteDelete y SQL crudo a los lugares autorizados.

El filtro/interceptor están preparados para recibir un tenant real, pero no autentican usuarios. Program.cs sigue registrando TenantProvisorio; no configura Identity ni el middleware de autenticación/autorización.

El publicador de Outbox y los consumidores siguen como dependencia transversal de mensajería; no forman parte del commit de aislamiento revisado.

La base del trabajo de Ezequiel permite avanzar con la integración. El cierre del CU-40 sigue requiriendo las adaptaciones anteriores, sesión real y pruebas combinadas. Los resultados de la rama por separado no certifican la combinación con lukovski.
