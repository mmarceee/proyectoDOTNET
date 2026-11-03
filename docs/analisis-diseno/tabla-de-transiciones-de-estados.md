ESTADOS: 

> **Actualización de diseño · 02/11/2026:** consultar la [decisión cerrada de entrega parcial](decisiones-entrega-parcial.md) y el [ADR-0007](ADR-0007-adaptacion-a-entrega-parcial.md). Esta tabla T1 a T19 documenta el flujo anterior. Debe adaptarse a transiciones por bulto y resumen derivado; conservarla aquí no autoriza a forzar NoEntregado sobre un envío que ya tiene entregas parciales.
>
> **Actualización · 03/11/2026:** la adaptación está en la sección [Entrega parcial: transiciones por bulto y resumen del envío](#entrega-parcial-transiciones-por-bulto-y-resumen-del-envío), al final. Los estados y las guardas de T1 a T19 siguen valiendo, pero ahora se aplican a cada bulto.

| ESTADO | TIPO | SIGNIFICADO | DONDE ESTA EL PAQUETE |
| :---- | :---- | :---- | :---- |
| Admitido | Inicial | El comercio cargó el envío y se calculó la tarifa. El operador aún no lo recibió.  | Con el comercio  |
| EnDeposito | Intermedio  | Recibido y escaneado en depósito, disponible para planificar.  | Depósito  |
| AsignadoARuta | Intermedio  | Incluido en una hoja de ruta aún no salida a la calle.  | Depósito  |
| EnTransito | Intermedio  | El repartidor confirmó la carga y salió con el envío.  | Vehículo  |
| NoEntregado | Intermedio  | Hubo un intento fallido; falta decidir reprogramar o devolver. Estado transitorio: el sistema decide en la misma operación si se reprograma o se devuelve (CU-18)  | Vehículo / regreso a depósito  |
| Reprogramado | Intermedio  | Tiene nueva fecha/franja de entrega, a la espera de ser asignado.  | Depósito  |
| EnDevolucion | Intermedio  | Se decidió devolverlo al comercio.  | Depósito / en camino al comercio  |
| Entregado | Terminal  | Entrega exitosa con prueba registrada.  | Destinatario  |
| Devuelto | Terminal  | Regresó al comercio.  | Comercio  |
| Extraviado | Terminal  | Se declaró perdido.  | Desconocido  |
| Cancelado | Terminal  | El comercio anuló el envío antes de que ingresara al depósito del operador.  | Con el comercio (nunca llegó a estar en poder del operador)  |

TABLA DE TRANSICIONES:

| \# | ORIGEN | ACCION | DESTINO | ACTOR | GUARDA(CONDICION ADICIONAL) | EVENTO GENERADO |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| T1 | *(ninguno)*  | Admitir  | Admitido  | Comercio (portal, importación o API)  | Dirección dentro de una zona de cobertura; tarifa calculada con la versión vigente del cuadro tarifario  | EnvioAdmitido  |
| T2 | Admitido  | RecepcionarEnDeposito  | EnDeposito  | Operario de depósito  | Bultos escaneados. Las discrepancias contra lo declarado **se registran**, no bloquean  | EnvioRecibidoEnDeposito  |
| T3 | EnDeposito  | AsignarARuta  | AsignadoARuta  | Despachador  | La ruta cumple restricciones (paradas, peso, volumen, franja). El envío no está en otra ruta (RF 15\)  | EnvioAsignadoARuta  |
| T4 | AsignadoARuta  | DesasignarDeRuta  | EnDeposito  | Despachador  | La ruta **aún no fue despachada**  | EnvioDesasignadoDeRuta  |
| T5 | AsignadoARuta  | ConfirmarCarga  | EnTransito  | Repartidor  | Escaneo de bultos contra la hoja de ruta; faltantes/sobrantes generan aviso (RF 19\)  | EnvioEnTransito  |
| T6 | EnTransito  | RegistrarEntrega  | Entregado  | Repartidor  | firma siempre obligatoria más las pruebas de la versión de reglas **del envío** (RF 20\)  | EnvioEntregado  |
| T7 | EnTransito  | RegistrarIntentoFallido  | NoEntregado  | Repartidor  | Motivo del catálogo del operador \+ evidencia. Incrementa el contador de intentos (RF 21\)  | IntentoFallidoRegistrado  |
| T8 | EnTransito  | ReintegrarADeposito  | EnDeposito  | Operario de depósito  | Rendición de un envío que salió pero **no llegó a intentarse**    | EnvioReintegradoADeposito  |
| T9 | NoEntregado  | Reprogramar  | Reprogramado  | Despachador / Sistema  | Intentos \< máximo del operador; se respeta el plazo entre intentos  y el motivo es reprogramable   | EnvioReprogramado  |
| T10 | NoEntregado  | IniciarDevolucion  | EnDevolucion  | Despachador / Sistema  | Intentos \= máximo, o política de devolución del operador o el motivo no es reprogramable   | DevolucionIniciada  |
| ~~T11~~ | ~~NoEntregado~~  | ~~Reprogramar~~  | ~~Reprogramado~~  | ~~Despachador (a pedido del destinatario, RF 26\)~~  | ~~La solicitud cumple las reglas del operador~~  | ~~EnvioReprogramado~~  |
| T12 | EnDeposito  | IniciarDevolucion  | EnDevolucion  | Despachador / Comercio  | El comercio o el operador deciden no entregar  | DevolucionIniciada  |
| T13 | Reprogramado  | AsignarARuta  | AsignadoARuta  | Despachador  | Mismas validaciones que T3, para la nueva fecha  | EnvioAsignadoARuta  |
| T14 | Reprogramado  | IniciarDevolucion  | EnDevolucion  | Sistema (worker) / Despachador  | Venció el plazo sin poder reasignarse  | DevolucionIniciada  |
| T15 | EnDevolucion  | ConfirmarDevolucion  | Devuelto  | Operario de depósito  | El comercio recibió el envío  | EnvioDevuelto  |
| T16 | EnDeposito, EnTransito, EnDevolucion  | DeclararExtravio  | Extraviado  | Administrador / Despachador  | Motivo obligatorio  | EnvioExtraviado  |
| T17 | Admitido  | CancelarEnvio  | Cancelado  | Comercio  | El envío **todavía no fue recibido en depósito** (no existe el evento `EnvioRecibidoEnDeposito`)  | EnvioCancelado  |
| T18 | EnDeposito  | Reprogramar  | Reprogramado  | Destinatario (seguimiento público) / Administrador (cambio programado de franjas, CU-04)  | Para el destinatario, se respeta la anticipación mínima del operador (`anticipacionReprogramacionHoras`) y la franja elegida se ofrece en la zona del envío (RF 26\). Para CU-04 con día de entrega definido, se cumplen la vigencia y la anticipación superior a 24 horas del cambio programado y se conserva el día de entrega  | EnvioReprogramado  |
| T19 | AsignadoARuta  | Reprogramar  | Reprogramado  | Destinatario (seguimiento público) / Administrador (cambio programado de franjas, CU-04)  | La ruta está **Planificada**. Para el destinatario, misma anticipación y franja que T18. Para CU-04, se cumplen la vigencia y la anticipación superior a 24 horas acordadas para el cambio programado. El envío sale de la ruta: se elimina su parada, como en T4. En CU-04 conserva el día de entrega definido, recibe la nueva franja y queda disponible para reasignar; si la ruta queda vacía, libera sus recursos, conservando la evidencia histórica  | EnvioReprogramado  |

| **T11** | ***Eliminada*** Inalcanzable: CU-18 aplica T7 y T9 o T10 en la misma transacción, así que el envío nunca queda en NoEntregado. | 

**Cualquier combinación estado \+ acción que no esté en la tabla se rechaza** con un error de dominio explícito (por ejemplo `TransicionInvalidaException`). Ejemplos que deben fallar: `Entregado → EnTransito`, `Admitido → Entregado`, `Devuelto → Reprogramado`, `EnDeposito → Cancelado` (una vez recibido en depósito la cancelación ya no está disponible con las reglas actuales.

* `EnTransito → Reprogramado`: el envío ya salió.  
* `AsignadoARuta → Reprogramado` con la ruta despachada


## Entrega parcial: transiciones por bulto y resumen del envío

| Dato | Valor |
| :---- | :---- |
| Versión y fecha | 1.1 · 03/11/2026 |
| Responsable | Cristian Reyes |
| Base | [Decisión cerrada de entrega parcial](decisiones-entrega-parcial.md) (secciones 2 a 5, pendiente 1 de la sección 6) y [ADR-0007](ADR-0007-adaptacion-a-entrega-parcial.md), sección 2.1 |
| Estado | Aprobada por el equipo el 03/11/2026. Implementada en `Bulto.Transicionar` y `Envio.RegistrarVisita` |

### 1. Idea central

**Las transiciones T1 a T19 se conservan, con los mismos estados y las mismas guardas, pero cada una se aplica a un bulto.** El envío ya no transiciona: al final de cada operación recalcula su resumen a partir de los estados de sus bultos (RF 31).

- El estado operativo vive en el bulto y usa los once estados de la tabla anterior. No hay un estado "Pendiente".
- Una operación puede afectar a varios bultos de un mismo envío (por ejemplo, una visita). Se valida cada bulto contra la tabla, y si uno falla se rechaza la operación completa sobre ese envío (atomicidad por visita, sección 4 de la decisión).
- El resumen se recalcula **una sola vez**, al final de la operación, en la misma transacción que los bultos, la evidencia y el Outbox.
- Ninguna operación decide mirando el resumen del envío: recepción, asignación, carga, entrega y devolución validan el estado del bulto.

### 2. Transiciones por bulto

| # | Origen → Destino del bulto | Bultos a los que se aplica | Diferencia con el flujo anterior |
| :---- | :---- | :---- | :---- |
| T1 | *(ninguno)* → Admitido | Todos, al crear el envío | Igual |
| T2 | Admitido → EnDeposito | Cada bulto escaneado en la recepción | Antes el envío entero pasaba a EnDeposito con el primer escaneo, y el escaneo del segundo bulto se rechazaba |
| T3 | EnDeposito → AsignadoARuta | Los bultos del envío disponibles para planificar | El bulto no puede estar en otra ruta activa (pendiente 3 de la decisión, con Planificación) |
| T4 | AsignadoARuta → EnDeposito | Los bultos de esa ruta | Igual, por bulto. También se aplica a los bultos que no se cargaron al confirmar la carga (T5) |
| T5 | AsignadoARuta → EnTransito | Los bultos escaneados en la carga | Si el operador **no admite** entrega parcial y falta un bulto del envío, no se carga ninguno de ese envío; los demás envíos de la ruta no se afectan (RF 33, sección 5 de la decisión) |
| T6 | EnTransito → Entregado | Los bultos entregados en la visita | Firma obligatoria y las pruebas de la versión de reglas del envío, asociadas a esos bultos |
| T7 | EnTransito → NoEntregado | Los bultos no entregados en la visita | Motivo del catálogo y evidencia. El contador de intentos es **por bulto** y sólo aumenta con un intento real |
| T8 | EnTransito → EnDeposito | Los bultos que vuelven sin haberse intentado | Igual, por bulto. No consume intentos |
| T9 | NoEntregado → Reprogramado | Los bultos de T7 | Se aplica en la misma operación que T7 (NoEntregado sigue siendo transitorio). Guarda: intentos del bulto < máximo, motivo reprogramable y la política de pendientes del operador lo permite (RF 33) |
| T10 | NoEntregado → EnDevolucion | Los bultos de T7 | Misma operación que T7. Guarda: intentos agotados, motivo no reprogramable o la política del operador indica devolución |
| T12 | EnDeposito → EnDevolucion | Los bultos en depósito del envío | Igual, por bulto |
| T13 | Reprogramado → AsignadoARuta | Los bultos reprogramados | Mismas validaciones que T3 |
| T14 | Reprogramado → EnDevolucion | Los bultos reprogramados | Igual, por bulto |
| T15 | EnDevolucion → Devuelto | Los bultos que recibe el comercio | Igual, por bulto |
| T16 | EnDeposito, EnTransito o EnDevolucion → Extraviado | El bulto declarado perdido | El alcance y la autorización del extravío individual quedan por confirmar (pendiente 4 de la decisión) |
| T17 | Admitido → Cancelado | **Todos los bultos del envío juntos** | La cancelación sigue siendo del envío: exige que ningún bulto se haya recibido |
| T18 | EnDeposito → Reprogramado | Los bultos del envío en EnDeposito | El destinatario reprograma el envío, no un bulto |
| T19 | AsignadoARuta → Reprogramado | Los bultos del envío en una ruta Planificada | Igual que T18 |

**Cualquier par origen → destino de bulto que no esté en esta tabla se rechaza** con `TransicionInvalidaException`, igual que antes. T11 sigue eliminada.

**Estados terminales del bulto (declarados, no deducidos por el nombre):** Entregado, Devuelto, Extraviado y Cancelado.

### 3. Resumen del envío

El envío guarda su resumen (para consultar y filtrar sin reconstruirlo) y nadie lo asigna directamente. Se calcula en este orden; gana la primera regla que se cumple:

| Orden | Condición sobre los bultos | Resumen |
| :---- | :---- | :---- |
| 1 | Todos están en Entregado | **Entregado** |
| 2 | Al menos uno está en Entregado | **Entregado parcialmente** |
| 3 | Al menos uno se recibió alguna vez (está en un estado distinto de Admitido y Cancelado), o el resumen ya era En operación | **En operación** |
| 4 | Ninguna de las anteriores | **Creado** |

- **Terminado:** verdadero cuando todos los bultos están en un estado terminal.
- **Contador:** bultos entregados / total de bultos originales, incluidos los devueltos, extraviados y cancelados.
- **Nunca vuelve a Creado:** ninguna transición de bulto vuelve a Admitido, y Cancelado no dice si el bulto se había recibido; por eso la regla 3 conserva En operación.
- **Presentación de un cierre sin entrega:** si Terminado es verdadero y no hay entregas, se muestra «Finalizado sin entrega» con el desglose. No es un quinto valor del resumen.
- **Hechos que no cambian el resumen:** pasar de 1/3 a 2/3 entregados mantiene Entregado parcialmente; una revisita fallida después de una entrega parcial también. Igual deben invalidar el seguimiento y actualizar el tablero (pendiente 7 de la decisión).

### 4. Pares inalcanzables

- La sección 3 de la decisión incluye el ejemplo «Todos cancelados después de recibir alguno». Con T17 como está, no puede ocurrir: un bulto recibido ya no se cancela. **El equipo aprobó el 03/11/2026 mantener T17 y tratar ese ejemplo como inalcanzable.**
- NoEntregado no se observa al terminar una operación: T7 siempre va seguida de T9 o T10 en la misma transacción.

### 5. Ejemplos de aceptación

Los diez ejemplos de la sección 3 de la decisión son las pruebas de aceptación del resumen: cada uno se convierte en una prueba unitaria del dominio, salvo el que se declare inalcanzable.

| Versión | Fecha | Cambio | Responsable |
| :---- | :---- | :---- | :---- |
| 1.0 | 03/11/2026 | Transiciones por bulto, cálculo del resumen, terminales declarados y consulta sobre la cancelación después de recibir | Cristian Reyes |
| 1.1 | 03/11/2026 | Aprobada por el equipo, con la consulta de la sección 4 resuelta: se mantiene T17 y el ejemplo de cancelación después de recibir es inalcanzable | Ezequiel Marcenal |
