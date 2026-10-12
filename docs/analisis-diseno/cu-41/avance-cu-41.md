# CU-41 · Quitar un envío de una ruta

Fecha: 12/10/2026. Responsable: Lucas. Implementación para desarrollo; cierre sujeto a sesión real e integración del Outbox transversal.

## Comportamiento

En el detalle de una ruta Planificada, **Editar paradas** habilita **Quitar** para cada envío. Se pide confirmación en un modal que muestra el número del envío y explica que volverá a En depósito. Cancelar cierra el modal sin enviar una operación de baja.

Confirmar elimina la parada, renumera las restantes, incrementa la revisión de la ruta y aplica T4 (`AsignadoARuta → EnDeposito`). Conserva zona, franja y fecha comprometidas del envío. El envío vuelve a estar disponible para asignarse a otra ruta, sujeto a los filtros y restricciones de CU-40.

Si se quita el último envío, la ruta permanece Planificada y libera sus reservas de repartidor y vehículo. Una nueva asignación a esa ruta debe volver a verificar la disponibilidad. El historial de validaciones de carga se conserva: quitar una parada no modifica ni elimina la evidencia previa.

El detalle muestra el **Historial de la ruta**, con los envíos quitados y las validaciones de carga en apartados propios. Cada retiro guarda número e identificador del envío, orden anterior, paradas restantes, revisión resultante, responsable y fecha/hora. Se registra en la misma transacción que T4, la parada y el Outbox. Las fechas se muestran como `dd/MM/yyyy HH:mm`, convertidas a UTC−3 sin mostrar la zona horaria en pantalla. No se fabrica una validación de carga al retirar un envío.

`GET /api/planificacion/rutas/{rutaId}/retiros?pagina=1` consulta los retiros con el aislamiento y control de acceso de la ruta. Las bajas hechas antes de este cambio no se reconstruyen automáticamente.

Una ruta Despachada, EnCurso o Finalizada no muestra la opción y el servidor rechaza la baja. Una revisión obsoleta devuelve conflicto: no se reintenta automáticamente ni se quita otra parada.

## Organización y transacción

- `Application/Features/QuitarEnvioDeRuta`: command y handler internos de Planificación.
- `Presentation/Features/QuitarEnvioDeRuta`: endpoint y parcial de confirmación, integrado al detalle existente.
- `Logistica.Http.Contracts/Planificacion/QuitarEnvioDeRutaRequest`: revisión esperada del cliente.
- `IEnviosModuleApi.DesasignarDeRutaAsync`: T4 aplicada por el módulo propietario del envío.
- `Envios.Contracts/Events/EnvioDesasignadoDeRuta`: datos del evento de integración.

API: `DELETE /api/planificacion/rutas/{rutaId}/envios/{envioId}`, con JSON `{ "revisionEsperada": 1 }`. Responde 200 con la ruta actualizada, 404 si la ruta no es visible, 409 ante estado o revisión incompatibles y 400 ante una solicitud inválida o un envío que no pertenece a la ruta. Reutiliza las restricciones de acceso del despachador y requiere antiforgery cuando se usa una cookie.

La operación usa la unidad de trabajo compartida y el mismo bloqueo corto del operador que CU-40. Después del bloqueo relee la ruta y verifica estado, revisión y pertenencia. Quitar la parada, guardar T4 y su EventoEnvio, registrar el mensaje durable y liberar reservas se confirman juntos. Un fallo al guardar Planificación revierte también los cambios ya guardados por Envíos.

No se revalida la capacidad al quitar: la carga disminuye, y una configuración cambiada o un recurso inactivo no deben impedir retirar un envío de una ruta todavía Planificada.

## Coordinación con el Outbox

Ezequiel y Cristian revisan el Outbox del CU-40 respecto de ADR-0002 y ADR-0003. Este cambio no modifica `OutboxMessage`, `ModuleDbContext`, su marcador o la configuración/migraciones de la tabla. Agrega `RegistrarDesasignacion` en el repositorio de Envíos para escribir `EnvioDesasignadoDeRuta.v1` mediante el adaptador actual, en la transacción existente.

Al incorporar el Outbox transversal, adaptar también **RegistrarDesasignacion** y las verificaciones de CU-41 al nuevo sobre. El evento actual conserva MessageId, OperadorId, ComercioId, EnvioId, RutaId, ResponsableId y OcurridoEn. El OperadorId y ComercioId actuales proceden del envío leído por el módulo propietario; la integración transversal deberá aplicar la política de metadatos del contexto original acordada en ADR-0002. No se certifica todavía el publicador sin inquilino ni la entrega por RabbitMQ.

## Pruebas

Las pruebas de integración usan PostgreSQL real. Cubren quitar una parada intermedia, conservar compromisos e historial, T4 y mensaje durable, ruta vacía y reutilización de recursos, rechazo de rutas no Planificadas, pertenencia, revisión obsoleta, confirmaciones simultáneas, carrera con la operación de dominio de despacho bajo la exclusión compartida, rollback completo, aislamiento por empresa y confirmación desde Razor con antiforgery. La carrera de despacho comprueba el contrato de concurrencia que deberá usar CU-43; no implementa su endpoint ni el resto de ese caso.

Las mismas pruebas verifican ahora el registro durable del retiro, su número y orden anterior, la revisión y paradas restantes, la visualización desde Razor con fecha UTC−3, el rollback del historial y el rechazo de consultas desde otra empresa. Las validaciones de carga anteriores permanecen idénticas.

Resultado de `scripts/cu40-en-docker.sh test`: compilación Release sin errores ni advertencias y **187 pruebas aprobadas** (40 unitarias, 54 de arquitectura y 93 de integración), ninguna fallida ni omitida. CU-41 incorpora doce escenarios de integración. Se ajustó la prueba de edición de datos del CU-40 para distinguir su acción CancelarDatos del botón Cancelar del nuevo modal.

El historial de retiros requiere la migración `HistorialRetirosCU41`, que crea `planificacion.RetirosEnvioRuta`. Las migraciones del Outbox transversal corresponden al trabajo coordinado con Ezequiel.
