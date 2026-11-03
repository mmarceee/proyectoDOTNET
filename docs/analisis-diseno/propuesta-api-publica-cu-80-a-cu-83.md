# Propuesta de diseño · API pública para comercios (CU-80 a CU-83)

| Dato | Valor |
| --- | --- |
| Fecha | 02/11/2026 |
| Responsable | Lucas Ottonello |
| Estado | CU-80, CU-81 y CU-83 implementados. CU-82 tiene listado y detalle. Queda integrar el progreso por bulto del ADR-0007. |
| Base inspeccionada | `337b222` en la revisión inicial. Implementación y auditoría del árbol de trabajo sobre `4e4c7a4` el 03/11/2026. |
| Objetivo | Opcional 7.4 completo, demostrable el 12/11. No se declara obtenido ningún punto. |

## 1. Fuentes y precedencia

- [Casos de uso, sección 2.7](casos-de-uso.md): comportamiento de CU-80 a CU-83 y decisión de pruebas mediante `EsPrueba` en sección 3.
- [Plan por hito, sección 4](plan-casos-de-uso-por-hito.md): las cinco condiciones son obligatorias; una implementación parcial no suma.
- [ADR-0001](ADR-0001-estilo-arquitectonico.md): monolito modular, vertical slices, Contracts entre módulos y único host web.
- [ADR-0002, secciones 2.4 y 2.5](ADR-0002-estrategia-de-multitenancy.md): clave por relación comercial, búsqueda excepcional por hash, claims, filtros y escrituras protegidas.
- [ADR-0003](ADR-0003-outbox-y-mensajeria.md): eventos por Outbox, Worker e idempotencia de consumidores.
- [ADR-0004](ADR-0004-resolucion-de-conflictos-de-sincronizacion.md): la idempotencia offline existente tiene otro identificador y alcance; no reemplaza la referencia comercial de CU-81.
- [ADR-0005](ADR-0005-modo-de-renderizado-de-blazor.md): Portal en Blazor WebAssembly; el registro conserva estado Propuesto.
- [ADR-0006](ADR-0006-estrategia-de-cache.md): caché e invalidación; no usarla para demorar la revocación de credenciales.
- [Revisión del cambio anunciado para el 29](revision-cambio-requerimientos-y-aclaraciones.md): contexto, aclaraciones y evidencia histórica. El documento docente fecha el cambio el 30/10.
- [Decisión cerrada de entrega parcial](decisiones-entrega-parcial.md) y [ADR-0007](ADR-0007-adaptacion-a-entrega-parcial.md): prevalecen sobre el modelo histórico del estado del envío. Diseño aceptado, implementación pendiente.

Esta propuesta desarrolla el alcance ya documentado. Los valores operativos, rutas auxiliares y políticas nuevas que aparecen a continuación son recomendaciones, no requisitos docentes adicionales.

## 2. Qué existe y qué falta

| Pieza | Hallazgo en el código inspeccionado |
| --- | --- |
| Aislamiento | `ModuleDbContext` y su interceptor protegen lecturas y escrituras. La autenticación por clave fija los claims de operador, comercio y ambiente. |
| CU-80 | Administración y el Portal permiten generar, listar y revocar claves. La autenticación de CU-81 resuelve el comercio y el operador a partir del hash y registra el último uso. |
| Alta | `CrearEnvioHandler` ya valida reglas, cobertura, tarifas y relación activa; guarda envío y evento por Outbox. |
| Idempotencia comercial | CU-81 reutiliza `ReferenciaComercio` y `CrearOObtenerAsync` de CU-11. La búsqueda y el índice único distinguen relación comercial y ambiente. |
| Pruebas | CU-81 persiste `EsPrueba` y lo excluye de las consultas operativas con un filtro separado del tenant. El aviso al comercio conserva la marca y Seguimiento omite el correo al destinatario. |
| Consultas | CU-82 tiene DTOs propios para listado, detalle e historial. Expone el número del envío y los códigos de bulto. |
| Errores | CU-81 devuelve 400 para datos inválidos y 422 si falta cobertura o tarifa. El alta del Portal mantiene el tratamiento general. |
| Límites | La API pública aplica un límite local por IP antes de autenticar y cuotas por relación y ambiente después. Devuelve `Retry-After` en los rechazos de ambas etapas. |
| Documentación | CU-83 publica una interfaz navegable en `/api/docs` y OpenAPI v1 generado desde las operaciones públicas. La consola usa rutas protegidas para claves de prueba. |
| Entrega parcial | No asumirla implementada: el código inspeccionado mantiene estado operativo por envío y el diseño nuevo exige adaptar consultas. |

## 3. CU-80: decisiones implementadas

1. **Propiedad:** Administración posee `ClaveApi`, asociada a `RelacionComercial`, con `OperadorId` y `ComercioId` y ambos marcadores de aislamiento. Los endpoints de gestión usan la cookie y el perfil UsuarioComercio; requieren operador seleccionado. La autenticación por clave vive en `Logistica.Api` y consulta Administración mediante Contracts, sin acceder a su DbContext.
2. **Secreto:** generar 32 bytes aleatorios criptográficos y un prefijo visible de ambiente; guardar hash SHA-256, prefijo y metadatos, nunca el secreto recuperable. Índice único por hash. El secreto sólo sale en la respuesta de creación; ni logs ni almacenamiento persistente del navegador deben conservarlo. El prefijo no autoriza ni determina por sí solo el tipo: vale el registro encontrado.
3. **Pantalla:** nombre, tipo, prefijo, creación, último uso y estado/revocación. Mostrar el secreto una vez, con copia; confirmar revocación. La rotación consiste en crear otra clave y después revocar la anterior, permitiendo solapamiento.
4. **Revocación:** lógica e irreversible; repetir la operación conserva el resultado. Consultar vigencia en la base en cada autenticación, sin caché positiva. Una solicitud autenticada después de confirmar la revocación debe recibir 401; una operación ya admitida puede terminar. Si se pretende cancelar también las operaciones en curso, es una exigencia adicional a especificar.
5. **Relación suspendida:** se bloquean el acceso público y la generación de claves. El listado y la revocación dependen de una sesión del Portal con operador seleccionado. Al refrescar la sesión, CU-03 retira al operador suspendido. La suspensión conserva las credenciales y la reactivación permite usar las que siguen vigentes.
6. **Último uso:** después de fijar tenant, con entidad rastreada y `SaveChanges`, como exige ADR-0002. Actualizar sólo ese campo y proteger concurrencia para que nunca deshaga una revocación ni retroceda la fecha.

La búsqueda previa al tenant sólo ignora el filtro `Tenant`, es sin tracking y está encapsulada en el camino de autenticación autorizado. El control posterior de relación activa se realiza ya con tenant. Las credenciales de API no dan acceso a las pantallas ni a los endpoints internos que requieren cookie, y una cookie no sustituye la clave en `/api/v1`.

## 4. Ambiente de pruebas y documentación ejecutable

Mantener la decisión existente: mismo despliegue y base, envíos marcados con `EsPrueba`. El ambiente se toma de la credencial autenticada; el body, los parámetros y los headers arbitrarios no pueden cambiarlo.

- Clave de producción ve sólo producción; clave de prueba ve sólo pruebas. Portal operativo, Backoffice, depósito, rutas, reportes y liquidaciones excluyen pruebas. Verificar también consultas de bultos, SQL y procesos automáticos que no atraviesen el filtro del envío.
- Filtro de ambiente separado de `Tenant`, con selección explícita del ambiente permitido. Nunca ignorar `Tenant` para acceder a pruebas. Ampliar esta regla a proyecciones o datos derivados que lo requieran.
- Propagar el carácter de prueba de forma durable a eventos/proyecciones y reintentos. El Worker puede generar avisos al sistema del comercio, pero no correos al destinatario ni efectos financieros u operativos reales. No cambiar silenciosamente la semántica de eventos existentes.
- El enlace de seguimiento de prueba también necesita aislamiento: no debe abrir el seguimiento público real. Resolverlo con una consulta de prueba autenticada y documentada antes de cerrar CU-81.
- La simulación de visitas no forma parte explícita de CU-80 a CU-83. No agregar un motor de simulación como condición inventada; usar fixtures para verificar parcial y avisos. Acordar si se desea simulación manual como ampliación.

**Prueba manual de CU-83.** Abrí `/api/docs` sin iniciar sesión. Navegá las operaciones y sus esquemas, y descargá el OpenAPI generado en `/api/openapi/v1.json`. Para ejecutar el ejemplo de alta, pegá una clave de prueba del Operador Demo y usá una referencia nueva. La primera ejecución devuelve 201 y la repetición devuelve 200. Copiá el número devuelto para abrir el detalle desde la misma página.

Las ejecuciones de la consola usan `POST /api/v1/pruebas/envios`, `GET /api/v1/pruebas/envios` y `GET /api/v1/pruebas/envios/{numero}`. Esas rutas reutilizan los mismos casos de uso y el servidor exige una clave de prueba. Una clave de producción recibe 403 aunque se invoque la ruta directamente. La página conserva el secreto en memoria y lo borra al salir o al pulsar **Borrar clave**. El OpenAPI enumera las rutas públicas canónicas y el enlace de seguimiento de prueba, y explica el destino de la consola.

## 5. CU-81: contrato e idempotencia

- `POST /api/v1/envios`, header recomendado `X-Api-Key`. Operador, comercio, origen API y ambiente se obtienen en el servidor; no se aceptan como autoridad desde el body.
- El endpoint público de Envíos pasa `ReferenciaComercio` a `CrearEnvioCommand` y llama a `CrearEnvioHandler.CrearOObtenerAsync`, que ya usa CU-11. El resultado `AltaEnvio.Creado` determina si corresponde 201 o 200.
- CU-81 exige referencia. Se reutiliza la normalización de CU-11, que recorta los espacios de los extremos, admite hasta 50 caracteres y distingue mayúsculas y minúsculas. El endpoint debe rechazar con 400 una referencia vacía o compuesta sólo de espacios, ya que el handler compartido admite altas sin referencia.
- La búsqueda del envío existente ocurre antes de cotizar. La API comprueba que la relación está activa antes de llamar al handler, también en los reintentos que devuelven el envío existente.
- El índice único y el manejo de `ReferenciaRepetidaException` resuelven el conflicto entre altas concurrentes. La búsqueda inicial y la búsqueda después del conflicto conservan el ambiente. Las pruebas HTTP comprueban un solo envío y evento por referencia en cada ambiente.
- Nuevo: 201 con número, tarifa, estado de alta, referencia y enlace. Existente: 200. `Location` apunta al seguimiento del envío creado. Clave inválida o revocada: 401. Validación: 400. Cobertura o tarifa: 422. Tasa: 429 con `Retry-After`.
- Diferenciar errores por códigos tipados o resultados de aplicación, no por comparación de textos de excepciones. Mantener coherencia con los consumidores actuales del handler.

El contrato de alta devuelve `Creado` como resultado de admisión, también cuando la referencia ya existe. El enlace de producción abre el seguimiento público firmado de CU-60. El enlace de prueba abre `GET /api/v1/pruebas/envios/{numero}/seguimiento` y exige una clave de prueba de la misma relación. El historial y el resumen de entrega parcial corresponden a CU-82.

Para probarlo con PowerShell 7, generá una clave de prueba en el Portal y usá el siguiente ejemplo con Operador Demo. La API usa la configuración habitual de `Seguimiento:ClaveToken` para firmar enlaces de producción.

```powershell
$clave = Read-Host 'Clave de API de prueba' -MaskInput
$cuerpo = @{
    referenciaComercio = 'PED-1042'
    destinatario = @{ nombre = 'Ana Pérez'; telefono = '099123456'; email = $null; documento = $null }
    direccion = @{ calle = 'Av. Italia'; numero = '1234'; localidad = 'Montevideo'; departamento = 'Montevideo'; codigoPostal = '11300'; referencia = $null }
    bultos = @(@{ pesoKg = 2; largoCm = 30; anchoCm = 20; altoCm = 10 })
} | ConvertTo-Json -Depth 5

Invoke-WebRequest http://localhost:8080/api/v1/envios -Method Post `
    -Headers @{ 'X-Api-Key' = $clave } -ContentType 'application/json' -Body $cuerpo -SkipHttpErrorCheck
```

Con una referencia nueva se obtiene 201. Al repetirla se obtiene 200 con el mismo número. Después de revocar la clave se obtiene 401. Antes de revocarla se puede comprobar el último uso en el Portal.

## 6. CU-82 y cambio de entrega parcial

`GET /api/v1/envios` devuelve `pagina`, `tamanoPagina`, `total` y `envios`. Ordena del más reciente al más antiguo. La clave determina el operador, comercio y ambiente, y comparte la cuota de CU-81.

| Parámetro | Comportamiento |
| --- | --- |
| `estado` | `Creado`, `EnOperacion`, `EntregadoParcialmente` o `Entregado`. La comparación ignora mayúsculas. |
| `fechaDesde`, `fechaHasta` | Fechas de creación inclusivas en formato `AAAA-MM-DD`, según la hora de Montevideo. |
| `referenciaComercio` | Coincidencia exacta tras recortar espacios en los extremos. Distingue mayúsculas. |
| `pagina`, `tamanoPagina` | Por defecto 1 y 20. Tamaño entre 1 y 100. Una página posterior a la última devuelve una lista vacía y conserva el total filtrado. |

`GET /api/v1/envios/{numero}` devuelve el resumen, los códigos de bulto y el historial en orden cronológico. Los eventos incluyen el tipo y la fecha. El detalle devuelve 404 para un número inexistente, ajeno o de otro ambiente. Una clave inválida recibe 401 y una solicitud que excede la cuota recibe 429. Los filtros inválidos reciben 400.

Con la misma variable `$clave` del ejemplo de CU-81 se puede consultar el listado y luego abrir un número devuelto en la respuesta.

```powershell
$base = 'http://localhost:8080/api/v1/envios'
$cabeceras = @{ 'X-Api-Key' = $clave }
$pagina = Invoke-RestMethod "${base}?estado=Creado&tamanoPagina=5" -Headers $cabeceras
$pagina.envios | Format-Table numero, referenciaComercio, estado, entregados, totalBultos
if ($pagina.envios.Count -gt 0) {
    $detalle = Invoke-RestMethod "$base/$($pagina.envios[0].numero)" -Headers $cabeceras
    $detalle.historial | Format-Table tipo, ocurridoEn
}
```

**Alcance de la primera proyección.** El código actual sigue registrando la admisión, la operación y los resultados terminales por envío. El resumen usa esos hechos y el total real de bultos. El estado de un bulto queda en `null` mientras falte un resultado individual o un resultado terminal del envío completo. Una recepción incompleta sigue el estado Admitido del agregado hasta completar la recepción. La integración con el resumen persistido de ADR-0007 deberá incluir el primer bulto recibido y el progreso de entrega individual.

El contrato v1 reserva los valores acordados para integrar el resumen del ADR-0007:

- `estado` usa los valores estables de la tabla de filtros. El filtro `EntregadoParcialmente` devuelve una página vacía hasta que el dominio pueda registrar ese resultado.
- `terminado`, `entregados`, `totalBultos` y el objeto `resultados`, con pendientes, devueltos, extraviados y cancelados.
- Detalle por bulto e historial relevante; expresar Finalizado sin entrega mediante el cierre y desglose acordados, sin inventar un quinto estado persistido.
- De 1/3 a 2/3 entregados debe cambiar la respuesta aunque el resumen siga siendo parcial. Si se cachean consultas, la invalidación debe cubrir ese hecho.

No trasladar directamente el enum histórico al contrato público para cambiarlo después. Acordar la proyección con Cristian, propietario del dominio, y los hechos/historial con Ezequiel. El versionado HTTP `/api/v1` y las versiones v1/v2 de avisos son contratos distintos; coordinar semántica sin confundir sus números.

## 7. Limitación de tasa

Los valores iniciales configurables son 120 solicitudes/minuto en producción (`ApiPublica:ProduccionPorMinuto`) y 30 en pruebas (`ApiPublica:PruebaPorMinuto`). Cada cuota se comparte por relación comercial y tipo, así que crear más claves no la multiplica.

Antes de consultar credenciales se aplica un límite de 600 solicitudes/minuto por IP (`ApiPublica:SolicitudesPorIpPorMinuto`) que abarca también intentos con claves ausentes o inválidas. Ambos límites devuelven 429 y `Retry-After`.

Para la entrega con una instancia puede usarse contador local, dejando explícito su alcance; el análisis previo no exige balanceo al equipo de tres. Con varias réplicas se necesita contador compartido o un punto de entrada que imponga una cuota global: no multiplicar silenciosamente los límites publicados. La estrategia distribuida y el comportamiento ante caída se decidirán si ése es el despliegue real, sin confundir disponibilidad de caché con autorización.

## 8. Orden de trabajo y evidencia de cierre

1. Acordar contrato público, alcance de idempotencia, política de relación suspendida y ejecución de pruebas de CU-83; registrar la decisión de arquitectura cuando se acepte. Esta propuesta no se autoproclama ADR aceptado.
2. Administración + Portal + autenticación: CU-80 completo y pruebas de aislamiento/revocación. Se puede avanzar sin esperar la implementación de parcial.
3. Ambiente de pruebas de extremo a extremo, incluido Outbox, Worker, enlaces y exclusiones operativas.
4. CU-81 sobre el alta idempotente de CU-11. Coordinar con Envíos la ampliación de la búsqueda y el índice a prueba y producción, y verificar el contrato HTTP.
5. CU-82 con proyección de parcial acordada; CU-83 se redacta desde el contrato y se verifica al integrar, no se deja para el último día.
6. Antes del 12/11, ejecutar demostración integrada y registrar resultados reales. No hay estimación de horas validada en esta revisión.

| Condición obligatoria | Evidencia mínima para declarar completo |
| --- | --- |
| API documentada y versionada | POST y GET públicos en `/api/v1`; contratos y todos los códigos de respuesta presentes en OpenAPI. |
| Claves por comercio | Dos operadores y dos comercios: listado, alta, revocación, acceso por clave y 404 ajenos; secreto sólo una vez; revocación y uso concurrentes sin reactivar la clave. |
| Ambiente de pruebas | Misma referencia en ambos ambientes sin colisión; ninguna lectura cruzada; pruebas ausentes en rutas/liquidaciones/Backoffice; Worker sin correo al destinatario y con aviso al comercio, también al reintentar. |
| Tasa diferenciada | Cuotas distintas comprobadas, 429 con espera, intentos inválidos limitados y rotación de claves sin multiplicar cuota. Aclarar número de instancias probado. |
| Documentación navegable | Acceso anónimo, sólo operaciones públicas, ejemplos ejecutables con prueba, producción rechazada por el servidor en el destino interactivo. |
| Coherencia con RF 31 a 36 | Consulta de 2/3 entregados y posterior cierre mixto: resumen, Terminado, total y estados de bulto correctos, sin falsas entregas completas. |

Además: dos POST simultáneos de la misma referencia crean un solo envío y un solo evento; errores 400/422 diferenciados; cookie sola rechazada en API pública y clave sola rechazada en gestión del Portal.

## 9. Pendiente de integración

Cristian comunicó que tiene el dominio por bulto en su PC. Quedan la migración y la publicación en `crisrey`. Los campos previstos en el envío son `Resumen`, `Terminado` y `BultosEntregados`. El total sale de la cantidad de bultos. Cada bulto tiene `Codigo`, `Estado` e `IntentosFallidos`. CU-82 leerá el resumen persistido y filtrará por `Resumen`, con el índice por operador indicado por Cristian.

Se propone compartir la lectura de `ConsultarEnviosHandler` y `ConsultarDetalleEnvioHandler` dentro de Envíos. Para reutilizarla en HTTP debe poder seleccionar el ambiente desde la clave, mantener `Tenant` y admitir la referencia exacta y la paginación de CU-82. La respuesta pública conserva sus DTOs y el historial de `EventoEnvio` hasta que se registren los hechos de visita por bulto. Los campos internos de Backoffice permanecen fuera de la API.

La migración propuesta inicializa los bultos antiguos a partir del estado global y calcula el resumen. Antes de integrarla conviene verificar un envío que sigue Admitido pero ya tiene una recepción parcial registrada. La decisión del ADR-0007 pide conservar esa evidencia cuando exista y reflejar la primera recepción como inicio de operación. Un envío antiguo entregado debe consultarse como Entregado con todos sus bultos entregados. El historial histórico sigue siendo del envío.

Cuando se publique el dominio y la migración se puede verificar la lectura HTTP con datos persistidos, incluido el filtro por EntregadoParcialmente y los contadores. Para demostrar el flujo completo falta la visita por bulto en sincronización. Debe verse el mismo envío con 2 de 3 entregados y un bulto pendiente, y luego con 2 de 3, un devuelto y `terminado: true`. Los escenarios de dominio informados por Cristian quedan por verificar sobre su rama. CU-83 ya funciona con el contrato HTTP actual. El opcional 7.4 queda abierto hasta cerrar la integración y la demostración conjunta.
