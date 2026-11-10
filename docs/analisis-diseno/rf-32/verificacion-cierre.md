# RF 32 · Verificación de cierre y prueba física

Fecha: 09/11/2026. Revisión sobre `549c1ae` con cambios locales de RF 32. Sin commit nuevo ni despliegue desde esta tarea. [Especificación vigente](especificacion-tecnica.md).

> **Actualización posterior al cerrar ADR-0007:** Lucas confirmó persistir toda la captura cifrada antes de confirmar (D9 del [cierre técnico](../cierre-tecnico-entrega-parcial.md)). Este registro conserva la verificación histórica de la alternativa de advertencia, que no acredita la nueva recuperación. El paso 4 de aceptación debe sustituirse por reabrir sin red y recuperar firma/foto/textos persistidos, comprobar que un cambio de entregados elimina la firma y que errores de cuota/concurrencia no informan guardado correcto. Implementación y aceptación de D9 pendientes.

## Cambio autorizado

En la revisión anterior Lucas había elegido mantener en memoria la captura sin confirmar y advertir antes de salir. Esa elección fue sustituida posteriormente por D9. En aquel alcance se incorporaron:

- Explicación de qué se guarda y estado visible de captura pendiente.
- Confirmación para abandonar la visita con firma, foto o textos; cancelar mantiene la pantalla.
- Advertencia nativa al recargar/cerrar; el navegador controla su disponibilidad y texto.
- Ausencia de advertencia por resultados de bulto ya persistidos o por una visita completa guardada.
- Conservación de la advertencia tras un error de captura/guardado. Bloqueo de navegación interna mientras se procesa el guardado.

No se añadió persistencia de formulario, esquema de base, endpoint ni migración. La invalidación de firma al cambiar entregados continúa vigente.

## Resultados ejecutados

| Verificación | Resultado | Evidencia local, relativa al repositorio |
| --- | --- | --- |
| Solución Release con cambios finales | 0 errores, 0 advertencias | `TestResults/rf32-cierre-build-final.log` |
| Publicación Release de la PWA | Correcta, incluyendo recorte de ensamblados y service worker publicado | `TestResults/rf32-cierre-publish.log`, `TestResults/rf32-cierre-publish/` |
| JavaScript | 72/72 aprobadas | `TestResults/rf32-cierre-js.log` |
| Unitarias .NET | 490/490 aprobadas | `TestResults/rf32-cierre/unitarias.trx` |
| Arquitectura | 56/56 aprobadas | `TestResults/rf32-cierre/arquitectura.trx` |
| Integración dirigida con PostgreSQL efímero | 127/127 aprobadas | `TestResults/rf32-cierre/rf32-cierre.trx` |
| Chrome 157.0.8103.38, viewport 390 × 844, build publicado y API simulada | 14 comprobaciones aprobadas, sin errores de página; ejecución final 09/11/2026 18:55, America/Montevideo | `TestResults/rf32-cierre-browser.log`, `TestResults/rf32-cierre-browser/resultado.json` |

La selección de integración incluye `Rf32PwaTests`, visitas por bulto de Ejecución, sincronización, carga, descarga de hoja, inicio/confirmación de rendición, entrega de avisos y eventos de bulto. Comprueba atomicidad, evidencia, compatibilidad, reenvíos, recargos y contratos relacionados. No se ejecutó toda la suite de integración de CI.

Las unitarias/arquitectura y la integración utilizan la compilación de los módulos realizada antes del cambio de presentación. La PWA modificada se volvió a publicar y la solución completa se volvió a compilar al final. Las pruebas de navegador ejecutan ese build publicado, no el binario anterior ni el servidor de desarrollo.

Los primeros intentos de compilación/testhost dentro del sandbox no completaron la ejecución. Se repitieron con el acceso requerido al proceso local; los TRX finales registran `Completed` con 490, 56 y 127 aprobadas respectivamente. No se cuentan los intentos abortados como pruebas válidas.

Se utilizaron PostgreSQL y sesiones de prueba. No se modificó la base de desarrollo, no se detuvo su API y no se enviaron avisos a comercios reales. Había cambios concurrentes en ADRs y caché que se preservaron; no se atribuyen a esta tarea.

## Reproducción

Ejecutar desde la raíz del repositorio con .NET 10, Docker y las dependencias de `scripts/tests` instaladas:

```powershell
dotnet build Logistica.slnx --artifacts-path TestResults/rf32-cierre-build -c Release
dotnet publish src/Logistica.Repartidor.Pwa/Logistica.Repartidor.Pwa.csproj --artifacts-path TestResults/rf32-cierre-build -c Release -o TestResults/rf32-cierre-publish
npm --prefix scripts/tests test
dotnet test tests/Logistica.UnitTests/Logistica.UnitTests.csproj --artifacts-path TestResults/rf32-cierre-build -c Release --no-build
dotnet test tests/Logistica.ArchitectureTests/Logistica.ArchitectureTests.csproj --artifacts-path TestResults/rf32-cierre-build -c Release --no-build
dotnet test tests/Logistica.IntegrationTests/Logistica.IntegrationTests.csproj --artifacts-path TestResults/rf32-cierre-build -c Release --no-build --filter 'FullyQualifiedName~Rf32PwaTests|FullyQualifiedName~Ejecucion.VisitaPorBultoTests|FullyQualifiedName~SincronizacionTests|FullyQualifiedName~ConfirmarCargaTests|FullyQualifiedName~DescargarHojaDeRutaTests|FullyQualifiedName~IniciarRendicionTests|FullyQualifiedName~ConfirmarRendicionTests|FullyQualifiedName~EntregarAvisosTests|FullyQualifiedName~EventosDeBultoTests'
node scripts/tests/repartidor-visita.browser.cjs
```

El ensayo de navegador requiere Chrome y un paquete Playwright disponible. Si no se encuentra por resolución normal de Node, definir `PLAYWRIGHT_MODULE_PATH` con la ruta del paquete instalado; esta tarea utilizó el runtime de herramientas disponible en el entorno. `PWA_PUBLISH_DIR` permite elegir otro directorio publicado; por defecto usa `TestResults/rf32-cierre-publish/wwwroot/repartidor`.

El script abre un servidor sólo en loopback, con identidad/hoja ficticias y API simulada. Instala el service worker real, cambia el navegador a offline, comprueba reapertura y compara identidad/contenido de la visita después de perder una respuesta. La garantía de idempotencia del negocio se comprueba por separado en PostgreSQL; el simulador no la reemplaza.

También comprueba cancelación/aceptación de salida con texto, firma sola, borrado, cambio del conjunto, foto sola, advertencia `beforeunload` y ausencia de aviso después del guardado. En otro contexto ficticio simula denegación de cámara y error de posición, carga una foto ilegible y comprueba que no se encola una visita con esos errores; después muestra un rechazo con el diagnóstico de su bulto. Las capturas finales son `visita-offline.png` y `visita-rechazada.png` en `TestResults/rf32-cierre-browser/`.

## Aceptación física que requiere dispositivo

Pendiente de ejecución en teléfono. El resultado automatizado de Chrome no se registra como una prueba de cámara física, GPS real, Worker o recepción externa de avisos.

**Preparación posterior del 09/11:** Lucas confirmó Android y eligió un entorno separado con datos demo. Se levantó `voluptasexpress-lan` con API/Worker actuales, base y mensajería independientes y HTTPS en `https://192.168.1.7:8443`. La CA pública se descarga en `http://192.168.1.7:8081/proyectoDOTNET-LAN.cer`. Se verificaron cadena/nombre TLS, login, sesión de repartidor y archivos publicados. La [guía LAN](../../../infra/lan/README.md) registra acceso, credenciales de demo, reinicio y certificado; esto prepara la prueba física, sin darla por ejecutada.

**Avance físico confirmado por Lucas:** Android `192.168.1.5` descargó la CA después de habilitar el firewall; Lucas confirmó su instalación y el acceso HTTPS. Se prepararon `ENV-000001`, sus tres bultos recibidos y una ruta despachada para el 09/11 mediante operaciones normales y autenticadas, sin SQL. Las etiquetas están en `TestResults/lan/etiquetas-rf32.pdf`. Escaneo físico, carga/inicio, captura offline, sincronización y reenvío todavía requieren ejecutar y registrar sus pasos; no se dan por aprobados por el mero ingreso al sitio.

Preparar un ambiente de prueba HTTPS accesible desde el teléfono, con versión de reglas nueva que admita parcial, un envío de tres bultos y una ruta asignada a un repartidor de prueba. Disponer de códigos físicos legibles, un receptor de avisos v2 de prueba y acceso al Backoffice. Evitar usar envíos reales para declarar entregas/extravíos de ensayo.

| Paso | Comprobación y evidencia a registrar |
| --- | --- |
| 1. Preparación | URL, commit/build, fecha/hora, modelo de teléfono, sistema/navegador, operador y envío de prueba. Instalar/abrir la PWA y descargar la hoja. |
| 2. Carga e inicio | Escanear los tres y confirmar; pulsar Comenzar ruta. Esperar a que el shell publicado esté instalado antes de cortar la red. |
| 3. Captura sin red | Activar modo avión; mantener ubicación del teléfono disponible. Escanear dos por cámara y asignar motivo al tercero. Mostrar los dos códigos antes de firmar; adjuntar foto requerida. |
| 4. Advertencia | Intentar volver: cancelar conserva captura. Aceptar la salida conserva resultados por bulto y requiere recapturar evidencia/textos al volver. Comprobar también recarga con advertencia cuando el navegador la permita. |
| 5. Firma | Firmar; cambiar el conjunto de entregados y comprobar que exige firma nueva, incluso volviendo a la selección original. |
| 6. Confirmación offline | Firmar/fotografiar y confirmar sin red. Registrar OperacionId; reabrir la PWA en modo avión. Debe mostrar la misma visita pendiente sin pedir recapturar evidencia confirmada. |
| 7. Sincronización | Recuperar conectividad y comprobar visita aplicada, 2/3 en Backoffice y público, evidencia vinculada a los dos entregados y motivo del tercero. Verificar aviso v2 de bultos y ausencia de falso EnvioEntregado v1. |
| 8. Reenvío | Reenviar el mismo payload/OperacionId en el ambiente de prueba o provocar pérdida de respuesta controlada. Debe responder YaAplicada sin nuevas entregas, intentos, evidencias aplicadas, avisos o recargos. |
| 9. Conflicto independiente | Con otro envío de prueba, registrar visita offline y cambiar su estado desde Backoffice antes de sincronizar. Debe rechazarla entera, conservar evidencia y mostrar el motivo por bulto; otro envío de la parada continúa. |
| 10. Fallos del teléfono | Denegar cámara: permitir código manual. Denegar GPS: informar error sin encolar visita ni quitar la advertencia. Comprobar sesión vencida y almacenamiento no disponible/lleno en un perfil de ensayo. |
| 11. Compatibilidad | Actualizar el shell con una cola pendiente sin borrar datos; comprobar que OperacionId, contenido y evidencia se conservan. |
| 12. Rendición | Entregados no se solicitan como retorno físico. En una variante con dos cargados, el tercero no cargado queda fuera de visita y no consume intento. |

Registrar resultado por paso y adjuntar evidencia de prueba en el repositorio o la tarjeta, sin copiar firma/documento reales a documentación pública. La implementación y verificación automatizada descritas corresponden al alcance anterior de advertencia; **la recuperación completa de D9 y la aceptación física/integrada están pendientes**. RF 34, tablero y liquidación continúan en sus tarjetas.
