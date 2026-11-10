# RF 32 · Aceptación integrada del 10/11/2026

Continuación del [ensayo en Android](ensayo-android-2026-11-10.md). Lucas pidió terminar RF 32 con dos verificaciones: conflicto de sincronización y entrega parcial offline cotejada con Backoffice, seguimiento público y receptor externo. No se presenta D9 como texto literal del docente: recuperar la captura antes de confirmar sigue siendo una decisión interna pendiente, separada del alcance de esta aceptación.

**Resultado: los dos ensayos aprobados. Aceptación del alcance original de RF 32 completada.** Esto no declara implementadas todas las decisiones posteriores del ADR-0007 ni cerradas RF 31 a 36 en conjunto.

## Entorno y alcance

Mismo Android y entorno aislado `voluptasexpress-lan`, origen `https://192.168.1.7:8443`. Ruta `01a1c711-0b2d-75ef-a8db-16eb04cae2c8`, dos envíos ficticios de tres bultos cada uno. Lucas confirmó escaneo de los seis, carga e inicio desde el teléfono. No se modificaron envíos de producción ni el ensayo anterior.

El receptor es `tools/Logistica.ReceptorDemo`, independiente de API y Worker, en loopback. Por autorización de Lucas se expuso sólo ese receptor mediante un túnel HTTPS temporal. Se configuraron suscripciones v1 y v2 de CambioDeEstado del comercio demo; ambas recibieron la prueba con HTTP 200. El túnel no publica el sistema de negocio ni reemplaza el despliegue en nube exigido para la defensa. No se documentan secretos ni URLs privadas del panel.

## Conflicto: aprobado

En `ENV-000003`, Lucas guardó sin Wi-Fi ni datos una visita de dos entregados y un tercero con motivo, firma y foto, y mantuvo el dispositivo desconectado. Se comprobó que el servidor aún no había recibido esa visita. Se declaró extraviado el envío por la pantalla autenticada de Backoffice, CU-21, antes de reconectar.

Lucas reconectó y confirmó que la PWA mostraba rechazo completo y motivo por bulto. La consulta independiente verificó:

- Operación `01a1c715-e2c5-7a89-9870-363f93b4262b`: Conflicto, BultoExtraviado, diagnóstico para los tres códigos.
- Firma y foto conservadas dentro de la operación rechazada; no se copiaron sus contenidos a documentación ni informes de prueba.
- Cero resultados de entrega aplicados, archivos de evidencia aplicada, recargos y eventos de entrega; cero intentos fallidos consumidos.
- Los tres bultos permanecen Extraviado. El seguimiento público muestra 0 de 3 entregados.

El cambio concurrente fue el extravío del envío completo disponible en CU-21. Esto verifica el diagnóstico por bulto sin incorporar extravío individual. El conflicto de un único bulto dentro de una visita mixta tiene pruebas automatizadas previas; no se atribuye esa variante al ensayo físico.

## Visita independiente y resultado integrado: aprobado

Lucas confirmó desde Android el guardado offline y posterior aplicación de `ENV-000004`, conservando la visita rechazada anterior. Operación `01a1c719-2414-711e-ade4-a8c08767bdc4`. Así se comprueba que el rechazo de otro envío no impide registrar esta visita; no se atribuye al teléfono una sincronización de ambas visitas en un único lote.

| Comprobación | Resultado |
| --- | --- |
| Backoffice | Página autenticada muestra 2 de 3 entregados y los tres códigos de bulto. |
| Portal | Entregado parcialmente; primero y segundo Entregado, tercero EnDevolucion. |
| Seguimiento público sin sesión | Entregado parcialmente, 2 de 3 bultos entregados; BTO 1 y BTO 2 Entregado, BTO 3 En devolución. Sin UUID ni códigos internos en sus bultos. |
| Evidencia aplicada | Dos resultados, dos archivos; firma sólo para los dos entregados, foto compartida con el resultado fallido. |
| Receptor externo | Tres avisos v2 por bulto: dos Entregado y tercero EnDevolucion con motivo RECHAZADO. HTTP 200 y HMAC verificado sobre el cuerpo exacto recibido. |
| Compatibilidad v1 | Recibió hechos de envío sin información de bulto. No recibió una entrega completa falsa. El estado global de compatibilidad EnDevolucion no sustituye el resumen parcial de público/Portal. |

## Reenvío: aprobado

Se reenvió el mismo OperacionId, dispositivo, secuencia, datos de negocio y archivos, reconstruidos de los registros aplicados; no es una captura byte a byte del JSON original ni recupera su correlación de traza. El servidor respondió YaAplicada.

Antes/después permanecieron una visita, dos resultados, dos archivos, cero recargos, los mismos ocho eventos de Envíos y siete registros de avisos para este envío (incluyen los dos de inicio de tránsito anteriores). Se cotejó también el receptor externo: mismos siete IDs y contenidos, sin recepciones adicionales ni IDs duplicados. Los intentos fallidos siguen 0, 0, 1. No se publicó EnvioEntregado.

## Evidencia local

Archivos ignorados por Git en `TestResults/lan`: `aceptacion-rf32.json`, `aceptacion-inicial.json`, `conflicto-provocado-rf32.json`, `aceptacion-conflicto-provocado.json`, `conflicto-verificado-rf32.json`, `aceptacion-final.json`, `avisos-antes-reenvio-rf32.json`, `avisos-recibidos-rf32.json`, `reenvio-aceptacion-verificado.json`. Los scripts de consulta no imprimen firmas, fotos ni datos del receptor. La configuración del receptor y sus secretos permanecen únicamente en archivos locales ignorados.

La ruta de este segundo ensayo permanece EnCurso: no se simula la declaración física del repartidor para cerrarla. La rendición y cierre del primer ensayo ya están acreditados en su registro. Las suscripciones temporales y el túnel del receptor se retiran después de registrar el resultado, conservando el historial.

Esta aceptación no acredita D9, adquisición de una posición GPS nueva sin red, pruebas de almacenamiento lleno o actualización del dispositivo, ni un recargo positivo de revisita de RF 33. La distinción evita convertir verificaciones adicionales en nuevos requisitos literales de RF 32.
