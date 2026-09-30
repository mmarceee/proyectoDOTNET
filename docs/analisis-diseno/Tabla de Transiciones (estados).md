ESTADOS: 

| ESTADO | TIPO | SIGNIFICADO | DONDE ESTA EL PAQUETE |
| :---- | :---- | :---- | :---- |
| Admitido | Inicial | El comercio cargó el envío y se calculó la tarifa. El operador aún no lo recibió.  | Con el comercio  |
| EnDeposito | Intermedio  | Recibido y escaneado en depósito, disponible para planificar.  | Depósito  |
| AsignadoARuta | Intermedio  | Incluido en una hoja de ruta aún no salida a la calle.  | Depósito  |
| EnTransito | Intermedio  | El repartidor confirmó la carga y salió con el envío.  | Vehículo  |
| NoEntregado | Intermedio  | Hubo un intento fallido; falta decidir reprogramar o devolver.  | Vehículo / regreso a depósito  |
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
| T6 | EnTransito  | RegistrarEntrega  | Entregado  | Repartidor  | Prueba de entrega según la regla **vigente** del operador; posición y hora del dispositivo (RF 20\)  | EnvioEntregado  |
| T7 | EnTransito  | RegistrarIntentoFallido  | NoEntregado  | Repartidor  | Motivo del catálogo del operador \+ evidencia. Incrementa el contador de intentos (RF 21\)  | IntentoFallidoRegistrado  |
| T8 | EnTransito  | ReintegrarADeposito  | EnDeposito  | Operario de depósito  | Rendición de un envío que salió pero **no llegó a intentarse**  | EnvioReintegradoADeposito  |
| T9 | NoEntregado  | Reprogramar  | Reprogramado  | Despachador / Sistema  | Intentos \< máximo del operador; se respeta el plazo entre intentos  | EnvioReprogramado  |
| T10 | NoEntregado  | IniciarDevolucion  | EnDevolucion  | Despachador / Sistema  | Intentos \= máximo, o política de devolución del operador  | DevolucionIniciada  |
| T11 | NoEntregado  | Reprogramar  | Reprogramado  | Despachador (a pedido del destinatario, RF 26\)  | La solicitud cumple las reglas del operador  | EnvioReprogramado  |
| T12 | EnDeposito  | IniciarDevolucion  | EnDevolucion  | Despachador / Comercio  | El comercio o el operador deciden no entregar  | DevolucionIniciada  |
| T13 | Reprogramado  | AsignarARuta  | AsignadoARuta  | Despachador  | Mismas validaciones que T3, para la nueva fecha  | EnvioAsignadoARuta  |
| T14 | Reprogramado  | IniciarDevolucion  | EnDevolucion  | Sistema (worker) / Despachador  | Venció el plazo sin poder reasignarse  | DevolucionIniciada  |
| T15 | EnDevolucion  | ConfirmarDevolucion  | Devuelto  | Operario de depósito  | El comercio recibió el envío  | EnvioDevuelto  |
| T16 | EnDeposito, EnTransito, EnDevolucion  | DeclararExtravio  | Extraviado  | Administrador / Despachador  | Motivo obligatorio  | EnvioExtraviado  |
| T17 | Admitido  | CancelarEnvio  | Cancelado  | Comercio  | El envío **todavía no fue recibido en depósito** (no existe el evento `EnvioRecibidoEnDeposito`)  | EnvioCancelado  |

**Cualquier combinación estado \+ acción que no esté en la tabla se rechaza** con un error de dominio explícito (por ejemplo `TransicionInvalidaException`). Ejemplos que deben fallar: `Entregado → EnTransito`, `Admitido → Entregado`, `Devuelto → Reprogramado`, `EnDeposito → Cancelado` (una vez recibido en depósito la cancelación ya no está disponible con las reglas actuales.