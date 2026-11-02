# CU-42 · Ordenar las paradas de una ruta

Implementado el 2/11/2026 para RF 16. Desde el detalle de una ruta Planificada con paradas, **Editar** (botón amarillo) muestra **Agregar envíos** y **Ordenar paradas**; este último abre `/backoffice/rutas/{id}/orden`.

## Comportamiento

1. Se muestra el orden actual, con franja efectiva de la visita, zona y código postal.
2. **Proponer orden automático** prepara una propuesta por inicio y fin de franja, zona (nombre, sin distinguir mayúsculas) y código postal. Sin franja se propone al final. Los empates conservan el orden anterior.
3. **Subir/Bajar** ajusta la propuesta en el servidor; no requiere JavaScript.
4. **Confirmar y guardar orden** es la única operación que persiste. Cancelar o salir descarta la propuesta.

Se conservan identificadores de paradas y miembros, agrupación de varios envíos, recursos, reservas, estados de envíos y evidencia de validaciones. Un cambio real incrementa una vez la revisión; guardar el mismo orden no crea una revisión artificial. No requiere migraciones.

Una visita agrupada usa su franja efectiva (intersección de compromisos). Normalmente comparte zona y código postal por dirección; si hay varias zonas, la clave muestra sus nombres distintos ordenados. Las zonas ausentes se resuelven por código postal; una referencia de zona que ya no está en los recursos se muestra como «Zona no disponible».

## Advertencia de franjas (A1)

Se recorre la propuesta manteniendo una hora mínima no decreciente: al encontrar una franja se espera hasta su inicio. Si esa hora alcanza o supera el fin de una franja posterior, se advierte y se exige confirmación explícita para guardar igualmente.

Ejemplo: visitar primero una parada de 14:00 a 18:00 y luego otra de 09:00 a 12:00 es incompatible incluso sin traslados. Franjas solapadas no se consideran automáticamente incumplidas.

Esta comprobación es una cota inferior: no calcula tiempos de viaje, duración de visitas ni una hora real de salida. La ausencia de advertencias no garantiza puntualidad. No se incorporó un optimizador geográfico ni se inventaron duraciones para simular una estimación.

## Seguridad y concurrencia

- Perfil Despachador y tenant de la sesión, con los filtros compartidos de Planificación.
- Antiforgery en la pantalla y en escrituras HTTP autenticadas con cookie.
- La lista debe ser una permutación completa de las paradas actuales: rechaza faltantes, duplicados y paradas ajenas antes de modificar.
- Revisión esperada obligatoria. Guardar toma el mismo bloqueo por operador que CU-40/41/43 y confirma dentro de la unidad de trabajo. Una edición, despacho o cambio de agrupación concurrente obliga a recargar.
- Sólo rutas Planificadas y no vacías. No se reordenan rutas despachadas, en curso ni finalizadas.

## Capas y endpoints

- Dominio: `OrdenadorParadas` propone, valida y advierte; `Ruta.OrdenarParadas` y `Ruta.MoverParada` protegen la secuencia y revisión.
- Aplicación: `OrdenarParadasHandler`, con datos geográficos de los contratos de Envíos y Administración. No accede a sus DbContext ni cambia envíos.
- Presentación: Razor Page propia y enlace desde el detalle de ruta. La propuesta y el movimiento manual no escriben en la base.

| Método | Ruta relativa a `/api/planificacion/rutas/{id}` | Uso |
| --- | --- | --- |
| GET | `/orden` | Orden actual y advertencias. |
| POST | `/orden/propuesta` | Propuesta automática, con `RevisionEsperada`. |
| POST | `/orden/prevalidacion` | Revisar `ParadaIds` en el orden solicitado, sin persistir. |
| POST | `/orden` | Guardar `RevisionEsperada`, `ParadaIds` y, cuando corresponda, `ConfirmarAdvertencias`. |

## Relación con RF 31 a 36

CU-42 puede desarrollarse en paralelo a la matriz de entrega parcial: opera sobre visitas existentes, no sobre resultados ni estados de bultos. RF 34 decidirá qué pendientes integran cada nueva ruta y cómo se agrupan; CU-42 podrá ordenar las paradas resultantes.

Si una modificación posterior agrega, elimina o reagrupa paradas, debe seguir incrementando la revisión de la ruta para invalidar propuestas anteriores. Un cambio de nombre, zona o código postal puede requerir regenerar la propuesta automática. No se modificaron los contratos offline de la PWA ni se adelantaron decisiones de la matriz de bultos.

## Verificación

Las pruebas de CU-42 cubren orden automático, empates, movimientos, advertencias, conservación de grupos, propuestas sin escritura, persistencia, confirmación explícita, listas inválidas, estado no editable, concurrencia, pantalla, permisos, aislamiento y antiforgery. Se ejecutan además regresiones de planificación y pruebas de arquitectura. Resultados locales del 2/11/2026:

- Compilación de la solución: correcta, sin advertencias ni errores.
- Pruebas unitarias de Planificación: 28 aprobadas.
- Pruebas de arquitectura: 56 aprobadas.
- Integración de CU-42: 11 aprobadas sobre PostgreSQL real en Docker, incluyendo pantalla, persistencia, confirmación explícita, concurrencia, revisión obsoleta, permisos, aislamiento y antiforgery.
- Regresiones de agrupar, quitar y despachar rutas: 35 aprobadas.
- Total de escenarios distintos aprobados en las ejecuciones de esta tarea: 130. No se ejecutó la suite completa de la solución.
- Incidencias de la verificación: el primer intento no superó el fixture por Docker no disponible; una vez recuperado el motor se corrigió la limpieza de una prueba que dejaba una ruta en curso y bloqueaba los recursos de las siguientes. La corrida final de los 11 casos de CU-42 aprobó completa.
- No se verificó visualmente la pantalla en un navegador con la aplicación desplegada.

Los logs y TRX locales quedan en `TestResults/cu-42/` (ignorados por Git). `cu42-final.trx` contiene los 11 casos nuevos aprobados; `integracion.trx` conserva la corrida con las 35 regresiones aprobadas y los fallos de limpieza ya corregidos en la corrida final. `unitarias.trx` y `arquitectura.trx` contienen las otras verificaciones aprobadas.

```powershell
dotnet test tests/Logistica.UnitTests/Logistica.UnitTests.csproj --filter "FullyQualifiedName~Planificacion"
dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj --filter "FullyQualifiedName~OrdenarParadasTests|FullyQualifiedName~ParadasAgrupadasTests|FullyQualifiedName~DespacharRutaTests|FullyQualifiedName~QuitarEnvioDeRutaTests"
dotnet test tests/Logistica.ArchitectureTests/Logistica.ArchitectureTests.csproj
```
