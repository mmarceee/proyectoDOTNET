# RF 32 · Ensayo físico en Android del 10/11/2026

**Circuito principal de visita confirmada validado en Android por Lucas:** reapertura sin red, sincronización y reenvío idempotente verificados contra el servidor demo. La recepción externa de avisos se verificó en el ensayo integrado posterior.

**Continuación posterior:** la [aceptación integrada](aceptacion-integrada-2026-11-10.md) completó conflicto, Backoffice/público y recepción externa con otros dos envíos, cerrando la aceptación del alcance original de RF 32. Los límites del primer ensayo descritos aquí se conservan como registro histórico.

## Entorno

Entorno independiente `voluptasexpress-lan`, con API/Worker publicados, PostgreSQL, Valkey y RabbitMQ propios. Origen `https://192.168.1.7:8443`; Android con CA de ensayo instalada. `localhost:5221` en PC corresponde a otro entorno.

Envío `ENV-000002`, tres bultos, parcial habilitada y foto/datos del receptor requeridos. Ruta `01a1c6c7-7441-72ce-a79d-c952a2d8e8c9`, del 10/11. Operación de visita: `01a1c6fa-4b9c-77d6-908c-890474db56a4`.

## Resultado

| Comprobación | Evidencia |
| --- | --- |
| Carga e inicio | Lucas confirmó escaneo de tres bultos, carga e inicio; servidor EnCurso con los tres EnTransito. |
| Visita offline | Dos entregados, motivo para el tercero, firma y foto. Lucas confirmó guardado pendiente sin Wi-Fi/datos. |
| Reapertura offline | Lucas recargó sin conexión y confirmó conservación del pendiente, resultados y motivo. La comprobación corresponde a una operación ya confirmada localmente. |
| Antes de sincronizar | Consulta independiente: 0/3 entregados y tres EnTransito. |
| Sincronización | Lucas recuperó Wi-Fi y confirmó el mensaje de aplicación; consulta independiente: 2/3 entregados, resumen Entregado parcialmente, Terminado=false. |
| Tercero | Motivo «El destinatario rechazó el envío», no reprogramable. Quedó EnDevolucion con un intento fallido. |
| Evidencia | Firma sólo en el resultado exitoso de los dos entregados; resultado fallido del tercero sin firma. Una única foto compartida. Dos archivos, firma y foto. No se incluyen datos del receptor ni imágenes en este documento. |
| Reenvío | Mismo OperacionId autenticado como repartidor demo: YaAplicada, sin nuevos resultados, archivos, estados/intentos ni eventos. |
| Rendición y cierre | Lucas confirmó la declaración desde Android. Se simuló la recepción del tercero mediante el formulario autenticado de depósito del entorno demo. Verificación posterior: ruta Finalizada, sin reserva activa; primero y segundo EntregadoEnCalle, tercero declarado y recibido, DevueltoADeposito. El envío conserva 2/3 entregados y el tercero EnDevolucion; cerrar la ruta no completa su devolución al comercio. |

### Reenvío y sus límites

Se reconstruyeron el mismo OperacionId, dispositivo, secuencia, datos de negocio y archivos aplicados. **No es una captura byte a byte del JSON original ni recupera su correlación de traza.** La identidad/payload local tras pérdida de red y reapertura tiene además las pruebas JavaScript y de navegador previas.

| Dato | Antes | Después |
| --- | --- | --- |
| Visitas | 1 | 1 |
| Resultados | 2: éxito de dos bultos y fallo de uno | Los mismos 2 |
| Archivos | 2 | Los mismos 2 |
| Recargos | 0 | 0 |
| Eventos de Envíos, incluidos los hechos anteriores de este envío | 8 | Los mismos 8 IDs |
| Intentos fallidos por bulto | 0, 0, 1 | 0, 0, 1 |

No se publicó EnvioEntregado. No hay entregas de avisos para este envío: no se acredita recepción externa ni un recargo positivo de revisita.

## Hallazgos y pendientes

- **Lector:** tras informar Lucas códigos deformados, se comprobaron las tres etiquetas renderizadas con el lector incluido. Se restringió la cámara a CODE-128 y se agregó quitar lecturas erróneas del borrador, sin alterar confirmaciones. Cámara simulada con etiqueta real: código correcto. Lucas después confirmó carga de tres bultos desde Android. No se aisló la causa óptica exacta del texto deformado inicial.
- **Ubicación:** el primer intento sin red falló aun con permiso habilitado. Se obtuvo posición con Wi-Fi y se volvió a desconectar para confirmar; el cliente acepta posición de hasta un minuto. Se valida guardado offline con posición reciente, **no adquisición de una posición nueva sin red o señal GPS**. No se omitió el requisito ni se inventaron coordenadas. El mensaje actual no distingue permiso, indisponibilidad y timeout.
- **Alcance del ensayo.** La reapertura comprobó la visita ya confirmada. El comportamiento del borrador se documenta en D9.
- **Otros:** pendientes conflicto físico durante desconexión, receptor de avisos y fallos/actualización del dispositivo. Atomicidad, parcial prohibida, concurrencia y compatibilidad tienen pruebas automatizadas, no ejecución física acreditada aquí.

Evidencia local en `TestResults/lan` (ignorado por Git): `antes-sincronizar.json`, `despues-sincronizar.json`, `reenvio-verificado.json`, `rendicion-preparada.json`, `cierre-rf32.json`, `lector-tests.log`, `lector-ui.log`, `lector-build.log`. El reenvío no imprime ni guarda los archivos de evidencia recuperados transitoriamente en memoria. La comprobación del cierre consulta la rendición persistida: la hoja de ruta deja de estar disponible para el repartidor una vez finalizada.

Referencias: [especificación](especificacion-tecnica.md), [verificación de cierre](verificacion-cierre.md), [D9](../anexo-tecnico-ADR-0007-entrega-parcial.md), [guía LAN](../../../infra/lan/README.md).
