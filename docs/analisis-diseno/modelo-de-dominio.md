# **Modelo de dominio**

*Plataforma Multioperador de Distribución de Última Milla — Taller de Sistemas de Información .NET (UTEC), edición 2026*

Este documento reúne el modelo de dominio completo como un único modelo, agrupado por tema para que se lea ordenado. Cada clase se define una sola vez, en la sección de su tema; el resto de las relaciones que la involucran se resuelven en la tabla de relaciones al final del documento, que cubre el modelo completo.

Convención de multitenancy: toda clase lleva un campo operadorId que la ata a su operador (inquilino), salvo Operador, Comercio y RelacionComercial, que se explican en la primera sección. Las relaciones hacia un enum no se listan como filas de relación: el enum es el tipo de un atributo, no una entidad relacionada.

Las notas junto a cada clase o relación reflejan decisiones tomadas durante el análisis (con su justificación en los requerimientos de la letra) o supuestos todavía abiertos para validar con el equipo y, si corresponde, con el docente.

# **Operadores, comercios, usuarios y configuración**

*Núcleo de multitenancy y de configuración versionada del dominio.*

## **Clases**

**Operador**

**Atributos**

* id: Guid

* nombre: string

* slug: string

* activo: bool

**Métodos**

* actualizarIdentidad(nueva: IdentidadVisual): void

* desactivar(): void

**IdentidadVisual**

*sin id propio — parte de Operador*

**Atributos**

* logoUrl: string

* colorPrimario: string

* colorSecundario: string

* emailContacto: string

* telefonoContacto: string

**Comercio**

*entidad global — NO lleva operadorId (puede operar con más de un operador)*

**Atributos**

* id: Guid

* razonSocial: string

* documentoFiscal: string (no se modifica)

* activo: bool

**Métodos**

* desactivar(): void

**RelacionComercial**

*representa "este comercio trabaja con este operador" — vínculo muchos a muchos*

**Atributos**

* id: Guid

* operadorId: Guid

* comercioId: Guid

* emailContacto: string (cada operador tiene el suyo para el comercio)

* estado: EstadoRelacion

* fechaAlta: DateTimeOffset

**Métodos**

* suspender(): void

* reactivar(): void

**Usuario**

**Atributos**

* id: Guid

* operadorId: Guid (solo si el perfil no es UsuarioComercio)

* comercioId: Guid (solo si el perfil es UsuarioComercio)

* email: string

* nombre: string

* perfil: Perfil

* activo: bool

**Métodos**

* cambiarPerfil(nuevo: Perfil): void

**Repartidor**

**Atributos**

* id: Guid

* operadorId: Guid

* usuarioId: Guid (opcional)

* nombre: string

* documento: string

* activo: bool

**Vehiculo**

**Atributos**

* id: Guid

* operadorId: Guid

* matricula: string

* tipo: string

* capacidadPesoKg: decimal

* capacidadVolumenM3: decimal

* largoCargaCm: decimal

* anchoCargaCm: decimal

* altoCargaCm: decimal

* activo: bool

**Métodos**

* admiteCarga(bulto: Bulto, pesoAcumuladoKg: decimal, volumenAcumuladoM3: decimal): bool

**Zona**

**Atributos**

* id: Guid

* operadorId: Guid

* codigo: string

* nombre: string

* codigosPostales: string\[\] (supuesto a confirmar — ver notas al final)

* activa: bool

**Métodos**

* cubre(codigoPostal: string): bool

* agregarFranja(desde: TimeOnly, hasta: TimeOnly, dias: DiaSemana\[\]): FranjaHoraria

**FranjaHoraria**

**Atributos**

* id: Guid

* zonaId: Guid

* horaDesde: TimeOnly

* horaHasta: TimeOnly

* dias: DiaSemana\[\]

**VersionTarifario**

*inmutable una vez publicada — RNF 6.6*

**Atributos**

* id: Guid

* operadorId: Guid

* numero: int

* vigenteDesde: DateTimeOffset

* vigenteHasta: DateTimeOffset (opcional)

* estado: EstadoVersion

**Métodos**

* publicar(desde: DateTimeOffset): void

* calcular(zonaId: Guid, pesoKg: decimal, volumenM3: decimal, modalidad: Modalidad): decimal

**ReglaTarifa**

**Atributos**

* id: Guid

* versionTarifarioId: Guid

* zonaId: Guid

* modalidad: Modalidad

* pesoDesdeKg: decimal

* pesoHastaKg: decimal

* volumenDesdeM3: decimal

* volumenHastaM3: decimal

* precioBase: decimal

**AjusteTarifa**

**Atributos**

* id: Guid

* versionTarifarioId: Guid

* tipo: TipoAjuste

* esPorcentaje: bool

* valor: decimal

* modalidad: Modalidad

**VersionReglas**

*inmutable una vez publicada — RNF 6.6*

**Atributos**

* id: Guid

* operadorId: Guid

* numero: int

* vigenteDesde: DateTimeOffset

* vigenteHasta: DateTimeOffset (opcional)

* estado: EstadoVersion

* maxIntentos: int

* plazoEntreIntentosHoras: int

* pruebaExigida: PruebaExigida

* politicaDevolucion: PoliticaDevolucion

* anticipacionReprogramacionHoras: int (anticipación mínima para que el destinatario reprograme — RF 26)

* maxParadasPorRuta: int (RF 14)

**Métodos**

* publicar(desde: DateTimeOffset): void

* plazoParaModalidad(modalidad: Modalidad): TimeSpan

* motivoValido(codigo: string): bool

**PruebaExigida**

*sin id propio — parte de VersionReglas*

**Atributos**

* (la firma del receptor es siempre obligatoria: no se configura)

* exigeFoto: bool

* exigeNombreYDocumento: bool

**PoliticaDevolucion**

*sin id propio — preliminar, la letra no define su contenido exacto*

**Atributos**

* devolverTrasIntentosAgotados: bool

* plazoDevolucionDias: int

**PlazoModalidad**

*sin id propio — parte de VersionReglas*

**Atributos**

* modalidad: Modalidad

* plazoHoras: int

**MotivoNoEntrega**

**Atributos**

* id: Guid

* versionReglasId: Guid

* codigo: string

* descripcion: string

* reprogramable: bool

* exigeEvidencia: bool

## **Enumerados**

**Perfil**  *«enum»*

Administrador, Despachador, Repartidor, OperarioDeposito, UsuarioComercio

**Modalidad**  *«enum»*

Estandar, Urgente

**EstadoVersion**  *«enum»*

Borrador, Vigente, Reemplazada

**TipoAjuste**  *«enum»*

Recargo, Bonificacion

**EstadoRelacion**  *«enum»*

Activa, Suspendida, Baja

# **Envíos, bultos y ciclo de vida**

*Es el agregado central del dominio. Usa la configuración vigente (tarifario y reglas) en el momento del alta del envío.*

## **Clases**

**Envio**

*raíz del agregado del envío*

**Atributos**

* id: Guid

* relacionComercialId: Guid

* referenciaComercio: string (identificador propio del comercio; único dentro de una misma RelacionComercial — sostiene la idempotencia de la importación masiva, RF 7\)

* numero: string

* tokenSeguimiento: string (para el enlace público de seguimiento; nunca se expone el id interno — RF 25\)

* destinatario: Destinatario

* direccion: Direccion

* zonaId: Guid

* franjaHorariaId: Guid (opcional)

* modalidad: Modalidad

* versionTarifarioId: Guid

* versionReglasId: Guid

* montoTarifa: decimal

* estado: EstadoEnvio

* creadoEn: DateTimeOffset

* esPrueba: bool (creado con una clave de prueba de la API pública; nunca llega a la operación real)

**Métodos**

* transicionar(nuevoEstado: EstadoEnvio, origen: OrigenEvento, responsableId: Guid): void — único punto que cambia el estado (RF 11\) y genera el EventoEnvio (RF 12\)

* agregarBulto(bulto: Bulto): void

**Bulto**

**Atributos**

* id: Guid

* envioId: Guid

* codigo: string (lo que se escanea)

* pesoKg: decimal

* largoCm: decimal

* anchoCm: decimal

* altoCm: decimal

**Métodos**

* volumenM3(): decimal

**Destinatario**

*sin id propio — dato del envío, no un registro compartido entre envíos (evita mezclar datos de distintos operadores y que un cambio afecte envíos históricos)*

**Atributos**

* nombre: string

* telefono: string

* email: string (opcional)

* documento: string (opcional)

**Direccion**

*sin id propio*

**Atributos**

* calle: string

* numero: string

* localidad: string

* departamento: string

* codigoPostal: string

* referencia: string (opcional)

* latitud: decimal (opcional)

* longitud: decimal (opcional)

**EventoEnvio**

*de solo inserción — inmutable (RF 12\)*

**Atributos**

* id: Guid

* envioId: Guid

* estadoAnterior: EstadoEnvio (opcional — nulo para el evento inicial del envío)

* estadoNuevo: EstadoEnvio

* ocurridoEn: DateTimeOffset

* origen: OrigenEvento

* responsableId: Guid (opcional)

* latitud: decimal (opcional)

* longitud: decimal (opcional)

* detalle: string (opcional)

**IntentoEntrega**

**Atributos**

* id: Guid

* envioId: Guid

* numeroIntento: int (la pareja envioId \+ numeroIntento es única)

* fechaHora: DateTimeOffset

* resultado: ResultadoIntento

* motivoNoEntregaId: Guid (obligatorio si resultado \= Fallido — invariante de dominio)

* evidencia: PruebaEntrega (opcional)

* observaciones: string (opcional)

**PruebaEntrega**

*sin id propio — evidencia de un intento, exitoso o fallido (RF 20 y RF 21\)*

**Atributos**

* firmaImagenUrl: string (opcional; aplica solo si el intento fue exitoso y la configuración del operador la exige)

* fotoUrl: string (opcional; aplica a intento exitoso o fallido)

* nombreReceptor: string (opcional; solo si fue exitoso)

* documentoReceptor: string (opcional; solo si fue exitoso)

* latitud: decimal

* longitud: decimal

* capturadaEn: DateTimeOffset

**Incidencia**

*raíz de agregado propia — no forma parte del agregado Envio*

**Atributos**

* id: Guid

* envioId: Guid

* tipo: TipoIncidencia

* descripcion: string

* estado: EstadoIncidencia

* creadaEn: DateTimeOffset

* resueltaEn: DateTimeOffset (opcional)

* resolucion: string (opcional)

**Métodos**

* resolver(resolucion: string): void

**Devolucion**

*raíz de agregado propia — no forma parte del agregado Envio*

**Atributos**

* id: Guid

* envioId: Guid

* motivo: string

* estado: EstadoDevolucion

* iniciadaEn: DateTimeOffset

* recibidaEnDepositoEn: DateTimeOffset (opcional)

**Métodos**

* marcarRecibida(): void

## **Enumerados**

**EstadoEnvio**  *«enum»*

Admitido, EnDeposito, AsignadoARuta, EnTransito, Entregado, NoEntregado, Reprogramado, EnDevolucion, Devuelto, Extraviado, Cancelado

**OrigenEvento**  *«enum»*

PortalComercio, Backoffice, AppRepartidor, Sistema, SeguimientoPublico, Api

**ResultadoIntento**  *«enum»*

Exitoso, Fallido

**TipoIncidencia**  *«enum»*

*enum fijo — la letra no exige catálogo configurable, a diferencia de MotivoNoEntrega*

Reclamo, Extravio, DañoEnBulto, Otro

**EstadoIncidencia**  *«enum»*

Abierta, EnProceso, Resuelta

**EstadoDevolucion**  *«enum»*

Pendiente, EnTransitoADeposito, RecibidaEnDeposito, Cerrada

# **Planificación, rutas, paradas y asignaciones**

## **Clases**

**Ruta**

*raíz de agregado*

**Atributos**

* id: Guid

* repartidorId: Guid

* vehiculoId: Guid

* fecha: DateOnly

* estado: EstadoRuta

* creadaEn: DateTimeOffset

* despachadaEn: DateTimeOffset (opcional)

**Métodos**

* agregarParada(envioId: Guid): void

* reordenar(criterio: CriterioOrden): void

* despachar(): void

**Parada**

**Atributos**

* id: Guid

* rutaId: Guid

* envioId: Guid

* orden: int

* estado: EstadoParada

* llegadaEn: DateTimeOffset (opcional)

## **Enumerados**

**EstadoRuta**  *«enum»*

Planificada, Despachada, EnCurso, Finalizada, Cancelada

**CriterioOrden**  *«enum»*

*preliminar — el criterio definitivo queda pendiente del ADR de despacho (no obligatorio, no resuelto aún)*

PorFranjaHoraria, PorZona

**EstadoParada**  *«enum»*

Pendiente, Completada, Fallida

## **Notas**

* RF 15 ("un envío no puede estar en dos rutas a la vez") es una invariante de aplicación/base de datos, no una cardinalidad del diagrama: un envío no puede tener más de una parada activa (estado distinto de Completada o Fallida) al mismo tiempo. Se sugiere reforzarla con un índice único filtrado en la base, además de la validación en la aplicación.

* La validación de restricciones de RF 14 (máximo de paradas, capacidad de peso/volumen vía Vehiculo.admiteCarga, compatibilidad de franja horaria) no vive en una sola clase: la ejecuta un servicio de dominio (por ejemplo PlanificadorDeRuta) que orquesta Ruta, Vehiculo y los Envio candidatos. No es una clase de este diagrama; se documenta en la arquitectura.

# **Ejecución en calle, intentos, pruebas e incidencias operativas**

*RF 18 (descarga offline) y RF 24 (resolución de conflictos de sincronización) no agregan clases de dominio: son comportamiento de la app y contenido del ADR de sincronización.*

## **Clases**

**EscaneoCarga**

*RF 19 — escaneo de bultos al cargar el vehículo*

**Atributos**

* id: Guid

* rutaId: Guid

* bultoId: Guid

* resultado: ResultadoEscaneo

* escaneadoEn: DateTimeOffset

**PosicionVehiculo**

*de solo inserción — RF 23*

**Atributos**

* id: Guid

* vehiculoId: Guid

* rutaId: Guid

* latitud: decimal

* longitud: decimal

* capturadaEn: DateTimeOffset

**Rendicion**

*raíz de agregado — RF 22, rendición al regresar al depósito*

**Atributos**

* id: Guid

* rutaId: Guid

* iniciadaEn: DateTimeOffset

* finalizadaEn: DateTimeOffset (opcional)

* observaciones: string (opcional)

**Métodos**

* cerrar(): void

**LineaRendicion**

**Atributos**

* id: Guid

* rendicionId: Guid

* bultoId: Guid

* estadoFinal: EstadoLineaRendicion

## **Enumerados**

**ResultadoEscaneo**  *«enum»*

Esperado, Sobrante

**EstadoLineaRendicion**  *«enum»*

EntregadoEnCalle, DevueltoADeposito, Extraviado

## **Notas**

* Un "faltante" de RF 19 no es una clase ni un estado guardado: EscaneoCarga solo registra lo que se escaneó. Un faltante se calcula como la diferencia entre los bultos esperados en la ruta y los que tienen un EscaneoCarga, no se persiste.

* PosicionVehiculo va a crecer mucho en volumen (reporte de GPS periódico); conviene pensar una política de retención al definir el diagrama de despliegue. Es un tema de infraestructura, no cambia el modelo de dominio.

* Rendicion (lado ruta/repartidor: qué bultos volvieron físicamente al depósito) y Devolucion (lado envío: un envío específico en proceso de devolución, sección de Envíos) son conceptos relacionados pero distintos.

# **Seguimiento público, avisos y notificaciones**

## **Clases**

**Notificacion**

*RF 27 — notificación automática al destinatario*

**Atributos**

* id: Guid

* envioId: Guid

* canal: CanalNotificacion

* tipo: TipoNotificacion

* destino: string (copiado de Envio.destinatario al crear la notificación — no es una relación, es una foto del dato en ese momento)

* estado: EstadoNotificacion

* creadaEn: DateTimeOffset

* enviadaEn: DateTimeOffset (opcional)

**SuscripcionAviso**

*raíz de agregado — RNF 6.9, configuración del comercio*

**Atributos**

* id: Guid

* relacionComercialId: Guid

* urlDestino: string

* tipoEvento: TipoEventoAviso

* secretoFirma: string (para que el comercio verifique el origen del mensaje)

* activa: bool

**Métodos**

* desactivar(): void

**EntregaAviso**

*historial de intentos de aviso a un comercio, con reintentos y cola de fallidos*

**Atributos**

* id: Guid

* suscripcionAvisoId: Guid

* eventoEnvioId: Guid

* intento: int

* estado: EstadoEntregaAviso

* codigoRespuesta: int (opcional)

* enviadoEn: DateTimeOffset

**Métodos**

* reintentar(): void

## **Enumerados**

**CanalNotificacion**  *«enum»*

Email, SMS, WhatsApp

**TipoNotificacion**  *«enum»*

CambioDeEstado, VentanaEstimada, SolicitudDeReprogramacion

**EstadoNotificacion**  *«enum»*

Pendiente, Enviada, Fallida

**TipoEventoAviso**  *«enum»*

CambioDeEstado, EntregaFallida, Devolucion

**EstadoEntregaAviso**  *«enum»*

Pendiente, Exitosa, Fallida, EnColaDeFallidos

## **Coordinación entre agregados (Envio, Devolucion, Incidencia, Notificacion, EntregaAviso)**

Los agregados no se llaman entre sí directamente. Cada uno publica eventos de dominio; una capa de aplicación (no de dominio) reacciona a esos eventos y coordina — el mismo patrón outbox que exige RNF 6.8, aplicado también puertas adentro del sistema.

| Evento | Lo dispara | Reacciona |
| :---- | :---- | :---- |
| EnvioTransicionoAEstado(NoEntregado, intentosAgotados=true) | Envio | Crea Devolucion |
| DevolucionRecibidaEnDeposito | Devolucion | Envio.transicionar(Devuelto, ...) |
| EnvioTransicionoAEstado(\*) | Envio | Crea Notificacion si el cambio es relevante para el destinatario |
| EnvioTransicionoAEstado(\*) | Envio | Crea EntregaAviso para cada SuscripcionAviso activa que matchee el tipo |

# **Depósito, devoluciones y liquidaciones/reportes**

*Los reportes de RF 30 (cumplimiento por zona/repartidor/comercio, motivos de no entrega, volumen por período, liquidación por comercio) no son clases nuevas: son consultas sobre datos de EventoEnvio, IntentoEntrega, Envio y Liquidacion, y se resuelven como proyecciones o vistas de solo lectura en la capa de arquitectura, no en el modelo de dominio.*

## **Clases**

**RecepcionDeposito**

*RF 10 — recepción del envío en depósito, verificación contra lo declarado por el comercio*

**Atributos**

* id: Guid

* bultoId: Guid

* recibidoEn: DateTimeOffset

* resultado: ResultadoRecepcion

* discrepancia: string (opcional)

**Liquidacion**

*raíz de agregado — RF 30*

**Atributos**

* id: Guid

* relacionComercialId: Guid

* periodoDesde: DateOnly

* periodoHasta: DateOnly

* estado: EstadoLiquidacion

* totalBruto: decimal

* totalAjustes: decimal

* totalNeto: decimal

* generadaEn: DateTimeOffset

**Métodos**

* cerrar(): void

**LineaLiquidacion**

**Atributos**

* id: Guid

* liquidacionId: Guid

* envioId: Guid

* concepto: string

* monto: decimal

## **Enumerados**

**ResultadoRecepcion**  *«enum»*

Conforme, ConDiscrepancia

**EstadoLiquidacion**  *«enum»*

Borrador, Emitida, Pagada

## **Notas**

* Devolucion ya está completamente definida en la sección de Envíos, bultos y ciclo de vida, junto al resto del ciclo de vida del envío, y no se repite acá.

# **Relaciones del modelo completo**

Tabla única con todas las relaciones del modelo, sin importar en qué sección se definió cada clase. No incluye las relaciones hacia un enum (el enum es el tipo de un atributo, no una entidad relacionada).

| Origen | Card. | Tipo | Card. | Destino |
| :---- | :---- | :---- | :---- | :---- |
| Operador | 1 | composición | 1 | IdentidadVisual |
| Operador | 1 | asociación | 0..\* | RelacionComercial |
| Comercio | 1 | asociación | 0..\* | RelacionComercial |
| Operador | 0..1 | asociación | 0..\* | Usuario |
| Comercio | 1 | asociación | 0..\* | Usuario |
| Operador | 1 | asociación | 0..\* | Repartidor |
| Repartidor | 0..1 | asociación | 0..1 | Usuario |
| Operador | 1 | asociación | 0..\* | Vehiculo |
| Operador | 1 | asociación | 0..\* | Zona |
| Zona | 1 | composición | 0..\* | FranjaHoraria |
| Operador | 1 | asociación | 0..\* | VersionTarifario |
| VersionTarifario | 1 | composición | 0..\* | ReglaTarifa |
| VersionTarifario | 1 | composición | 0..\* | AjusteTarifa |
| ReglaTarifa | 0..\* | asociación | 1 | Zona |
| Operador | 1 | asociación | 0..\* | VersionReglas |
| VersionReglas | 1 | composición | 0..\* | MotivoNoEntrega |
| VersionReglas | 1 | composición | 1..\* | PlazoModalidad |
| VersionReglas | 1 | composición | 1 | PruebaExigida |
| VersionReglas | 1 | composición | 1 | PoliticaDevolucion |
| RelacionComercial | 1 | asociación | 0..\* | Envio |
| Envio | 1 | composición | 1..\* | Bulto |
| Envio | 1 | composición | 1 | Destinatario |
| Envio | 1 | composición | 1 | Direccion |
| Envio | 1 | composición | 1..\* | EventoEnvio |
| Envio | 1 | composición | 0..\* | IntentoEntrega |
| IntentoEntrega | 1 | composición | 0..1 | PruebaEntrega |
| Envio | 1 | asociación | 0..\* | Incidencia |
| Envio | 1 | asociación | 0..1 | Devolucion |
| Envio | 0..\* | asociación | 1 | Zona |
| Envio | 0..\* | asociación | 1 | VersionTarifario |
| Envio | 0..\* | asociación | 1 | VersionReglas |
| EventoEnvio | 0..\* | asociación | 0..1 | Usuario |
| IntentoEntrega | 0..\* | asociación | 0..1 | MotivoNoEntrega |
| Ruta | 1 | composición | 1..\* | Parada |
| Ruta | 0..\* | asociación | 1 | Repartidor |
| Ruta | 0..\* | asociación | 1 | Vehiculo |
| Parada | 0..\* | asociación | 1 | Envio |
| Rendicion | 1 | composición | 0..\* | LineaRendicion |
| EscaneoCarga | 0..\* | asociación | 1 | Ruta |
| EscaneoCarga | 0..\* | asociación | 1 | Bulto |
| PosicionVehiculo | 0..\* | asociación | 1 | Vehiculo |
| PosicionVehiculo | 0..\* | asociación | 1 | Ruta |
| Rendicion | 1 | asociación | 1 | Ruta |
| LineaRendicion | 0..\* | asociación | 1 | Bulto |
| EntregaAviso | 0..\* | asociación | 1 | SuscripcionAviso |
| Notificacion | 0..\* | asociación | 1 | Envio |
| SuscripcionAviso | 0..\* | asociación | 1 | RelacionComercial |
| EntregaAviso | 0..\* | asociación | 1 | EventoEnvio |
| Liquidacion | 1 | composición | 1..\* | LineaLiquidacion |
| RecepcionDeposito | 0..\* | asociación | 1 | Bulto |
| Liquidacion | 0..\* | asociación | 1 | RelacionComercial |
| LineaLiquidacion | 0..\* | asociación | 1 | Envio |

# **Supuestos y preguntas abiertas**

Puntos del modelo que dependen de una decisión del equipo (o, en algún caso, de una aclaración del docente) y que pueden hacer cambiar alguna de las vistas anteriores.

* Cobertura de Zona: se modeló codigosPostales: string\[\] por simplicidad. Alternativa más realista pero más costosa: polígono geográfico. Pendiente de confirmar con el equipo.

* Comercio ↔ Operador: se corrigió a relación muchos a muchos a través de RelacionComercial, según la letra ("un mismo comercio puede operar con más de un operador logístico").

* TipoIncidencia: se modeló como enum fijo del sistema. La letra no exige que sea configurable por operador, a diferencia de MotivoNoEntrega. Queda como catálogo cerrado salvo que el equipo decida lo contrario.

* CriterioOrden (Planificación y despacho, RF 16): el criterio definitivo de ordenamiento de paradas está pendiente del ADR de despacho (no es uno de los ADR obligatorios de la letra). Mientras tanto, PorFranjaHoraria y PorZona son valores preliminares.

* Resolución de RF 15 (un envío sin dos paradas activas simultáneas): se recomienda invariante de aplicación \+ índice único filtrado en base de datos. Pendiente de definir con qué motor de base (según lo que se decida en el ADR de persistencia).

* Coordinación entre Envio y Devolucion/Incidencia: resuelta mediante eventos de dominio y una capa de aplicación, en línea con el patrón outbox de RNF 6.8. El detalle técnico (tipo de outbox, mecanismo de publicación) se define en el ADR correspondiente.