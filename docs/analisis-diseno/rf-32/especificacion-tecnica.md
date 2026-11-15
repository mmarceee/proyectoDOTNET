# RF 32 · Especificación técnica de visita por bulto sin conexión

Fecha: 09/11/2026. Alcance: PWA, persistencia local, evidencia y consumo de contratos. Complementa la [decisión funcional, sección 4](../decisiones-entrega-parcial.md), [ADR-0004, sección 2.11](../ADR-0004-resolucion-de-conflictos-de-sincronizacion.md) y [ADR-0007](../ADR-0007-adaptacion-a-entrega-parcial.md). No redefine la tarjeta del servidor ni las reservas de RF 34.

## 1. Dependencias y límites

RF 31 proporciona estado e intentos individuales, resumen del envío y registro atómico de visita. RF 33 proporciona reglas versionadas, catálogo de motivos y política de parcial/revisitas. El envío conserva su versión incluso al sincronizar después de una publicación de reglas.

La PWA depende de `Logistica.Http.Contracts`, no de los proyectos internos de los módulos. El servidor resuelve identidad, operador, autorización, pertenencia a ruta y concurrencia. Los contratos internos de Envíos (`ConsultarParaVisitaAsync` y `RegistrarVisitaAsync`) pertenecen a la implementación del servidor; el cliente no los invoca directamente.

No se implementan aquí liberación ni reasignación individual de reservas, conciliación de custodia, extravío individual, cambios del tablero o cobro en liquidación. Sí se comprueba que la visita no genere efectos duplicados al integrarse con esos consumidores.

## 2. Precondiciones y recorrido

1. Con conexión, iniciar sesión de repartidor y descargar la hoja de una ruta propia. Instalar previamente el service worker del build publicado para poder reabrir el shell sin red.
2. Confirmar carga y pulsar **Comenzar ruta**. El inicio puede quedar pendiente localmente; su operación se sincroniza después de las cargas y antes de las visitas. Después de iniciar se cierra la carga.
3. Desde la hoja, abrir un envío. Cada envío dentro de la parada mantiene una visita independiente. La visita comprende los bultos en tránsito de esa ruta más los autorizados por cargas confirmadas en el dispositivo cuando la hoja aún no reflejó la salida.
4. Escanear los entregados por cámara o introducir el código del lector. Rechazar códigos ajenos al alcance y duplicados. Para cada otro bulto del alcance elegir un motivo individual del catálogo descargado. Los no cargados y resueltos se muestran fuera del alcance.
5. Mostrar los códigos entregados antes de firmar. Validar evidencia y datos del receptor. Confirmar sólo después de persistir de forma atómica la operación completa y el borrador confirmado.
6. Mostrar **pendiente** hasta el recibo del servidor. Un fallo de red conserva la operación para reintento. Mostrar **aplicada** con Aplicada/YaAplicada y **rechazada** con Conflicto/Invalida.

No se ofrece una segunda confirmación del mismo envío y ruta si ya existe una operación pendiente, aplicada o rechazada. Una revisita real en otra ruta es una operación distinta. Un rechazo se revisa con despacho; no se corrige fabricando una operación nueva ni aplicando sólo sus líneas válidas.

## 3. Reglas de captura y evidencia

| Condición | Validación |
| --- | --- |
| Entregado | Debe haberse escaneado/registrado su código. En el payload se representa con motivo nulo. |
| No entregado dentro del alcance | Requiere `MotivoId` individual válido para la versión del envío. No se asigna motivo por defecto. |
| Uno o más entregados | Firma obligatoria. Nombre y documento obligatorios sólo si lo exige la regla del envío. |
| Cambia el conjunto entregado | Invalidar firma, aun si se vuelve posteriormente al conjunto original. Cambiar sólo un motivo sin alterar los entregados no modifica su alcance. |
| Ningún entregado | No se exige ni se envía firma de recepción o datos del receptor. |
| Regla de entrega exige foto y hay entregados | Foto obligatoria. |
| Algún motivo exige evidencia | Foto obligatoria, incluso si no hay entregados. |
| Foto de visita | Una única foto de presencia compartida por los resultados de esa visita. No reemplaza la firma. |
| Parcial deshabilitada | Todos los pendientes que corresponde entregar o ninguno. No basta con entregar todos los cargados si falta otro pendiente. |
| Archivos | PNG/JPEG hasta 1 MB cada uno; la foto se reduce a un lado máximo de 1600 píxeles y se comprime antes de guardar. |
| Textos | Nombre hasta 200 caracteres, documento hasta 50, observaciones hasta 1000. |
| Ubicación | Coordenadas válidas obligatorias para confirmar. Si el dispositivo no entrega posición se informa error y no se encola la visita. |

La evidencia aplicada se vincula en servidor a los `BultoIds` del resultado correspondiente. Una visita posterior conserva la evidencia anterior y aporta la propia. Firma y foto no se trasladan a otro envío.

## 4. Persistencia y concurrencia local

Se reutiliza IndexedDB `logistica-repartidor-v1` y sus almacenes existentes. El espacio local se separa por usuario, operador y repartidor; la clave del borrador diferencia ruta y `visita:{EnvioId}`. Se conserva la recuperación de borradores antiguos que colisionaban con carga, sin borrar la base ni alterar operaciones confirmadas.

El contenido personal de hoja, borrador, operación y diagnóstico se cifra mediante AES-GCM. El token del espacio se comprueba al escribir para impedir que una captura iniciada bajo otra sesión termine guardándose en la identidad actual. No se guardan firma/foto en Cache Storage.

Cada modificación persistida compara la revisión esperada. La confirmación escribe el borrador confirmado y la operación con evidencia en la misma transacción. Dos pestañas no pueden confirmar la misma revisión. También se comprueba la rendición confirmada concurrentemente. Si falla el almacenamiento no se comunica guardado correcto ni se pierde la revisión anterior.

Las operaciones confirmadas son inmutables para el cliente: actualizar la hoja no cambia su identificador ni su contenido. Las hojas schema 1/2 siguen siendo legibles y las operaciones antiguas se conservan. El schema 3 exige estados de bulto, regla booleana de parcial y un manifiesto compatible con rendición; una descarga incompleta no reemplaza la copia anterior.

### Recuperación del formulario antes de confirmar

**Decisión D9 del 15/11/2026.** Al recargar se recuperan los resultados y motivos por bulto del borrador cifrado. La firma, la foto, el nombre y documento de quien recibe y las observaciones permanecen en memoria hasta confirmar. Si se abandona o recarga la pantalla, se vuelven a capturar. Si cambia el conjunto de bultos entregados, se pide una firma nueva. Al confirmar, la operación completa y su evidencia se guardan cifradas para sincronizar. Véase [D9](../anexo-tecnico-ADR-0007-entrega-parcial.md#d9-borrador-de-visita-y-evidencia-al-confirmar).

La pantalla informa qué campos se conservan y pide confirmación al salir por navegación interna. Recargar, cerrar o navegar a otro sitio activa la advertencia nativa del navegador, sujeta a sus reglas de interacción y cierre.

La advertencia se activa si hay evidencia o textos en memoria. Los resultados individuales ya guardados y una visita confirmada se pueden recuperar sin volver a capturarlos. Si falla la validación de evidencia o ubicación, o falla el guardado, se conserva la advertencia. Durante el guardado se bloquea la navegación interna.

## 5. Contrato HTTP

Se utiliza `POST /api/repartidor/sincronizacion`, hasta 20 operaciones por lote, con credenciales del mismo origen. El cuerpo contiene `Operaciones` y el contador opcional de pendientes. La PWA nueva genera únicamente `RegistrarVisita` para resultados en domicilio.

```json
{
  "operaciones": [{
    "operacionId": "<UUID generado una vez al confirmar>",
    "dispositivoId": "<UUID del dispositivo>",
    "secuencia": 12,
    "tipo": "RegistrarVisita",
    "rutaId": "<UUID de ruta>",
    "envioId": "<UUID de envio>",
    "ocurridoEn": "2026-11-09T15:00:00Z",
    "latitud": -34.9,
    "longitud": -56.1,
    "visita": {
      "bultos": [
        { "codigoBulto": "ENV-001-1", "motivoId": null },
        { "codigoBulto": "ENV-001-2", "motivoId": null },
        { "codigoBulto": "ENV-001-3", "motivoId": "<UUID del motivo>" }
      ],
      "firma": { "tipoContenido": "image/png", "contenidoBase64": "<base64>" },
      "foto": { "tipoContenido": "image/jpeg", "contenidoBase64": "<base64>" },
      "nombreReceptor": null,
      "documentoReceptor": null,
      "observaciones": null
    }
  }]
}
```

El ejemplo ilustra la forma; los valores entre ángulos deben sustituirse por UUID/base64 válidos. Se omite aquí `CorrelacionId` opcional, generado en el cliente para trazabilidad.

| Respuesta por operación | Acción local |
| --- | --- |
| Aplicada / YaAplicada | Conservar recibo y resultados para consulta; retirar la operación de la cola pendiente. |
| Conflicto / Invalida | Conservar operación completa y evidencia, motivo, detalle y diagnóstico cifrado. Mostrar que no se aplicó ningún resultado. No reintentar automáticamente como una visita nueva. |
| Fallo de transporte, timeout o falta de respuesta | Conservar y reenviar el mismo identificador y contenido. |
| 401 / 403 | Bloquear el acceso al espacio, conservar sus datos cifrados y pedir nuevo ingreso. |
| Error HTTP o respuesta no reconocida | Mantener la operación sin darla por aplicada. |

El diagnóstico por bulto incluye código y motivo: `BultoExtraviado`, `BultoEnDeposito`, `BultoYaResuelto`, `BultoFueraDeRuta`, `BultoAjenoAlEnvio`, `BultoRepetido` o `BultoSinResultado`. Los motivos generales de parcial/evidencia y pertenencia se muestran además cuando correspondan. No se requiere que las líneas válidas aparezcan como rechazadas individualmente: la atomicidad rechaza la visita completa.

## 6. Garantías del servidor consumidas por la PWA

El servidor valida todos los bultos y las reglas del envío antes de aplicar. Si se acepta, bultos, resumen, evidencia aplicada, operación, recargo cuando corresponda y Outbox se confirman juntos. Si se rechaza, se conserva la operación con evidencia y diagnóstico y no se consumen intentos ni se generan efectos de entrega.

`OperacionId` es la clave idempotente: perder la respuesta y repetir no duplica resultados ni Outbox. Los consumidores conservan su idempotencia para evitar avisos duplicados. Las operaciones de otros envíos continúan independientemente; las posteriores dependientes de una rechazada se tratan según ADR-0004.

El servidor mantiene la traducción de operaciones antiguas por envío a visitas uniformes. No se reescriben esas operaciones pendientes desde la PWA nueva.

## 7. Aceptación y evidencia

La matriz de escenarios está en el [análisis de cierre, sección 6](analisis-de-cierre.md#6-criterios-verificables-de-aceptación-conjunta). El [registro de verificación](verificacion-cierre.md) distingue resultados ejecutados y comprobaciones físicas pendientes. El cierre de RF 32 requiere:

- Pruebas JavaScript de persistencia, reenvío, revisión concurrente, alcance, motivos y errores.
- Compilación de la PWA y pruebas de integración dirigidas con PostgreSQL aislado.
- Ensayo del shell publicado con navegador sin red, reapertura y captura de evidencia.
- Ensayo físico en teléfono con cámara/GPS y recuperación de conectividad, más recepción externa del aviso para aceptación conjunta. La simulación del navegador no acredita este último punto.

Mantener fuera del cierre de esta tarjeta los pendientes de RF 34 y tablero, sin ocultarlos ni condicionar la implementación local a una liberación de reservas inexistente.
