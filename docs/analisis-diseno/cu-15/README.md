# CU-15 · Etiquetas y reimpresión en depósito

Fecha: 07/11/2026. Responsable: Lucas Ottonello. Implementación asistida por Codex.

## Alcance funcional

El usuario del comercio descarga las etiquetas desde la confirmación del alta o el detalle de Mis envíos. Desde el listado selecciona varios envíos para obtener un único PDF. La selección se limita a la página visible y se limpia al paginar o aplicar filtros.

Ampliación autorizada por Lucas en este chat: operario de depósito, administrador y despachador pueden reimprimir desde el detalle del Backoffice cuando los paquetes llegan sin etiquetas. El listado existente permite identificar el envío por número, referencia o destinatario. Antes de pegar cada etiqueta deben comprobar qué paquete corresponde a cada bulto; una ambigüedad requiere aclaración con el comercio. La descarga no registra recepción ni modifica datos.

Una página de 100 × 150 mm por bulto, con márgenes de 5 mm: logo y nombre del operador, número de envío, posición sobre el total original, modalidad, destinatario, dirección con referencia, zona y Code 128 con el código real legible debajo. Sin teléfono, documento, token público, tarifa ni recargos. Sin logo se conserva el nombre. En RF 35, `BTO 1` es sólo una presentación pública y no sustituye al código operativo.

Se imprimen todos los bultos originales, sin filtrar por resultado ni renumerar los pendientes. Se admite reimpresión de entregados y parcialmente entregados. Sólo se excluyen envíos cancelados. El orden de bultos se obtiene del sufijo numérico persistido, no del orden textual del código. El orden de los envíos sigue la selección recibida, eliminando duplicados.

Máximo de 100 entradas de envío y 200 etiquetas por petición. Lotes vacíos, inválidos, demasiado grandes o con cancelados devuelven 400. Si hay un envío inexistente o ajeno, 404 genérico y ningún PDF parcial. La disponibilidad se informa al Portal, pero el servidor vuelve a validar al descargar. Sin operador o sesión válida no se accede a datos.

## Decisión técnica local

Se adopta QuestPDF 2026.9.1 y ZXing.Net 0.16.11, con versiones centralizadas en Directory.Packages.props. QuestPDF está configurado con licencia Community para aprendizaje/evaluación en este laboratorio. Una eventual explotación comercial debe revisar su elegibilidad; no se afirma que QuestPDF sea MIT. ZXing.Net tiene licencia Apache-2.0.

Alternativas consideradas: HTML e impresión del navegador no garantizan el PDF y tamaño físico uniforme requeridos; un navegador headless agrega un proceso y dependencias al contenedor; un servicio remoto enviaría datos personales fuera del sistema. QuestPDF ofrece composición directa en .NET y ZXing produce barras vectoriales sin System.Drawing. Se usa la fuente Lato incluida por QuestPDF, sin depender de fuentes de Windows.

Fuentes primarias: [barras en QuestPDF](https://www.questpdf.com/api-reference/barcodes.html), [licencia Community](https://www.questpdf.com/license/community.html), [ZXing.Net](https://github.com/micjahn/ZXing.Net).

Esta decisión se limita al adaptador de CU-15; no cambia el estilo arquitectónico ni declara un ADR nuevo aceptado por el equipo.

## Arquitectura y contratos

- ADR-0001: slice `Application/Features/ImprimirEtiquetas`, puertos IEtiquetasReader e IGeneradorEtiquetas, adaptadores en Infrastructure y endpoints en Presentation. El handler compone consultas y generación; no usa EF, HTTP ni QuestPDF.
- ADR-0002: sesión y filtros Tenant de operador/comercio. La consulta conserva también el filtro Prueba. No usa IgnoreQueryFilters. Logo consultado por el operador actual.
- ADR-0003: lectura sin escrituras, sin eventos, Outbox, colas ni Worker.
- ADR-0004: no agrega operaciones offline; imprimir no representa una visita ni recepción.
- ADR-0005: Portal WebAssembly consume la API; la biblioteca PDF queda en el servidor. El estado de ese ADR sigue siendo propuesto.
- ADR-0006: sin caché de PDF; respuesta privada con `Cache-Control: no-store`.
- ADR-0007: identidad persistente por bulto y total original; ningún cambio en reglas de parcial, visitas o recargos.

Administración expone `LogoParaEtiquetaAsync` por su Contracts: tipo de contenido y bytes del logo actual, o null. Se reutilizan MarcaDelOperadorAsync y ConsultarZonasAsync. No hay llamadas HTTP entre módulos ni lectura del repositorio ajeno. La proyección de etiqueta evita cargar historial, evidencias y datos privados innecesarios.

| Ruta | Acceso | Resultado |
| --- | --- | --- |
| GET `/api/envios/portal/{numero}/etiquetas` | Comercio con operador elegido | PDF individual |
| POST `/api/envios/portal/etiquetas` con `{ "numeros": ["ENV-000001", "ENV-000002"] }` | Comercio con operador elegido | PDF único del lote |
| GET `/api/envios/{numero}/etiquetas` | OperarioDeposito, Administrador o Despachador | Reimpresión individual |

Content-Type `application/pdf`, Content-Disposition `attachment`, nombre `etiquetas.pdf`, `no-store` y `nosniff`. No se persiste archivo ni se agregan tablas/migraciones. El Portal muestra los errores de descarga en el lugar de la acción. Backoffice ofrece una descarga directa protegida por la misma política que controla su botón.

## Verificación

Pruebas: `EtiquetasPdfTests` verifica decodificación Code 128 y generación con doce bultos, dirección extensa, códigos largos, logos SVG/PNG/JPEG y ausencia de logo. `ImprimirEtiquetasTests` verifica descarga individual/lote, orden numérico, deduplicación, reimpresión parcial con el total original, inmutabilidad, cancelados, límites, filtros por comercio/operador/ambiente y permisos reales con sesión. La suite de arquitectura controla las dependencias.

Para exportar muestras de QA, definir `CU15_QA_DIR` a un directorio temporal y ejecutar `dotnet test tests/Logistica.UnitTests --filter FullyQualifiedName~EtiquetasPdfTests`. Revisar el número y tamaño de páginas y renderizar con Poppler; verificar orden 1 a 12 y textos sin recorte.

Resultados ejecutados el 07/11/2026:

- Solución completa: compilación sin errores ni advertencias.
- 453 pruebas unitarias y 56 de arquitectura aprobadas.
- 15 pruebas de integración aprobadas: ImprimirEtiquetasTests (8), ConsultarEnviosPortalTests (3) e IdentidadVisualTests (4), con PostgreSQL efímero en Docker.
- Las 7 pruebas de EtiquetasPdfTests también aprobadas dentro de un contenedor Linux .NET 10, con repositorio y caché NuGet montados sólo para lectura y artefactos en `/tmp`.
- pypdf confirmó 12 páginas de 100 × 150 mm, posiciones 1 a 12 y texto esperado. Inspección visual de páginas 1 y 12 y variante sin logo, renderizadas con Poppler, sin recortes ni superposiciones.
- Chrome headless con el mismo ZXingBrowser de la PWA decodificó las imágenes completas de esas tres muestras y obtuvo los códigos esperados. El módulo JS de descarga produjo `etiquetas.pdf` con bytes idénticos al PDF generado.

Al compilar se encontró evidencia local antigua en `tests/Logistica.IntegrationTests/TestResults` con fuentes C# generadas de otros proyectos. Se excluyó `TestResults/**/*.cs` de Compile en ese proyecto, sin eliminar las evidencias ni modificar código ajeno al problema.

Pendiente de aceptación física: imprimir a tamaño real (100 %, sin ajustar a hoja), escanear con cámara PWA y con el lector real del depósito, y comprobar que ambos reconocen el mismo bulto. La decodificación automatizada no sustituye esta comprobación del hardware y la impresora disponibles.
