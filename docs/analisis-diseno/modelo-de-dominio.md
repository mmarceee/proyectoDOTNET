# **Modelo de dominio**

*Plataforma Multioperador de Distribución de Última Milla — Taller de Sistemas de Información .NET (UTEC), edición 2026*

Este documento reúne el modelo de dominio completo como un único modelo, agrupado por tema para que se lea ordenado. Cada clase se define una sola vez, en la sección de su tema; el resto de las relaciones que la involucran se resuelven en la tabla de relaciones al final del documento, que cubre el modelo completo.

Convención de multitenancy (ADR-0002 v2.1): toda clase lleva un campo operadorId que la ata a su operador (inquilino), salvo Operador y Comercio, que son globales. RelacionComercial lleva operadorId y comercioId y se filtra por ambos (ADR-0002, sección 2.6). Usuario lleva operadorId o comercioId, pero no tiene filtro de inquilino (ver la nota de Usuario).

Segundo nivel de aislamiento: además de operadorId, llevan comercioId las clases que consulta el Portal o la API del comercio: RelacionComercial, ClaveApi, Envio, Bulto, EventoEnvio, IntentoEntrega, Incidencia, Devolucion, SuscripcionAviso, EntregaAviso, Liquidacion y LineaLiquidacion. Estos campos no se repiten en cada clase, salvo donde ayudan a entenderla.

Las relaciones hacia un enum no se listan como filas de relación: el enum es el tipo de un atributo, no una entidad relacionada.

Las notas junto a cada clase o relación reflejan decisiones tomadas durante el análisis (con su justificación en los requerimientos de la letra) o supuestos todavía abiertos para validar con el equipo y, si corresponde, con el docente.

# **Operadores, comercios, usuarios y configuración**

*Núcleo de multitenancy y de configuración versionada del dominio.*

## **Clases**

**Operador**

**Atributos**

* id: Guid

* nombre: string

* slug: string

* zonaHoraria: string (la usa el cierre diario, CU-70)

* activo: bool

**Métodos**

* actualizarIdentidad(nueva: IdentidadVisual): void

* desactivar(): void

**IdentidadVisual**

*sin id propio — parte de Operador*

**Atributos**

* logoArchivoId: Guid (los archivos se guardan en una tabla de PostgreSQL — Propuesta de stack, sección 3.9; CU-07)

* colorPrimario: string

* colorSecundario: string

* emailContacto: string

* telefonoContacto: string

**Comercio**

*entidad global — NO lleva operadorId (puede operar con más de un operador)*

**Atributos**

* id: Guid

* razonSocial: string

* documentoFiscal: string (no se modifica; único en toda la plataforma)

**Métodos**

* corregirRazonSocial(nueva: string): void — sólo lo usa el propio comercio (CU-01, CU-02)

*No tiene estado activo/inactivo: la baja la hace cada operador sobre su RelacionComercial (CU-01). Si un operador desactivara el Comercio, lo desactivaría también para los demás operadores.*

**RelacionComercial**

*representa "este comercio trabaja con este operador" — vínculo muchos a muchos. Se filtra por operadorId y por comercioId: el personal de un operador sólo ve las relaciones de su operador, y un comercio sólo ve las suyas (ADR-0002, sección 2.6)*

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

* darDeBaja(): void — baja lógica; no se permite con envíos en estados no terminales (CU-01, A6)

**Usuario**

*se implementa sobre ASP.NET Core Identity. No tiene filtro de inquilino, porque el inicio de sesión ocurre antes de conocer el tenant; los listados del Backoffice filtran explícitamente por operador. Un usuario de comercio es compartido entre los operadores con los que trabaja el comercio: un operador no lo desactiva, sino que suspende su RelacionComercial (CU-02). Figura como excepción en el ADR-0002 (secciones 2.4 y 4)*

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

**ClaveApi**

*credencial de la API pública (CU-80 y CU-81). Identifica a la RelacionComercial: de ella salen el operador y el comercio de la solicitud (ADR-0002). La búsqueda por hash ocurre antes de conocer el tenant y es el único acceso que ignora el filtro: es un caso autorizado del ADR-0002, sección 2.4*

**Atributos**

* id: Guid

* operadorId: Guid

* comercioId: Guid

* relacionComercialId: Guid

* nombre: string

* tipo: TipoClaveApi

* hash: string (nunca se guarda la clave en claro)

* prefijo: string (primeros caracteres, para mostrarla en el listado)

* creadaEn: DateTimeOffset

* ultimoUsoEn: DateTimeOffset (opcional)

* revocadaEn: DateTimeOffset (opcional)

**Métodos**

* revocar(): void

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

* codigosPostales: string\[\] (cada código postal pertenece a una sola zona activa — CU-04)

* activa: bool

**Métodos**

* cubre(codigoPostal: string): bool

* agregarFranja(desde: TimeOnly, hasta: TimeOnly, dias: DiaSemana\[\], vigenteDesde: DateOnly): FranjaHoraria

* reemplazarFranja(franjaAnteriorId: Guid, desde: TimeOnly, hasta: TimeOnly, dias: DiaSemana\[\], vigenteDesde: DateOnly): FranjaHoraria — crea una nueva franja y cierra la vigencia de la anterior, conservando su historial. La aplicación coordina la comprobación de anticipación, reprogramación, revalidación y notificaciones de los envíos afectados.

**FranjaHoraria**

**Atributos**

* id: Guid

* zonaId: Guid

* horaDesde: TimeOnly

* horaHasta: TimeOnly

* dias: DiaSemana\[\]

* vigenteDesde: DateOnly (primer día de vigencia, incluido)

* vigenteHasta: DateOnly (opcional; último día de vigencia, incluido; sin valor significa sin fin programado)

* reemplazaAId: Guid (opcional; identifica explícitamente la franja anterior de la misma zona que esta reemplaza)

**Vigencia de franjas (acordada al definir CU-40 el 09/10/2026):** el Administrador del operador programa cambios de franjas por zona con una fecha de entrada en vigencia. Cada cambio crea una nueva franja, vinculada explícitamente a la anterior mediante reemplazaAId, sin sobrescribir sus horarios históricos. La anterior termina el día previo a vigenteDesde de su reemplazo. Las consultas de cobertura horaria se resuelven para el día de entrega o de la ruta. Los compromisos afectados desde esa fecha se reprograman y revalidan con más de 24 horas de anticipación hasta el inicio más temprano entre la franja anterior y la nueva, conservando el historial y notificando al destinatario y al comercio. No se modifican rutas Despachadas o EnCurso; se propone una vigencia posterior si impiden el cambio. Los pedidos nuevos no postergan el cambio y sólo pueden usar franjas atendidas para su fecha. Los envíos sin día definido resuelven su franja al planificar. Las firmas del flujo por lote y su coordinación técnica con rutas se definen en la especificación técnica de CU-40; el comportamiento transaccional y la aplicación de T19 se detallan en las decisiones acordadas.

**Confirmación del cambio programado (acordada el 09/10/2026):** la aplicación comprueba todos los envíos y rutas afectados y confirma las nuevas vigencias, reprogramaciones, cambios necesarios en la planificación y mensajes de aviso en Outbox en una sola transacción. Si cualquier compromiso o conflicto impide el cambio, se conserva la configuración anterior sin reprogramaciones parciales. Las notificaciones se entregan después de confirmar y se reintentan si fallan, sin deshacer la operación. Las firmas objetivo de los contratos se definen en la especificación técnica de CU-40; los envíos ya asignados a rutas Planificadas se tratan mediante T19, conforme a las notas de Planificación.

La [especificación técnica de CU-40](cu-40/especificacion-tecnica-cu-40.md), sección 8, concreta el flujo por lote, los contratos, reemplazos sucesivos y el tratamiento de envíos sin día definido. La implementación de esta ampliación corresponde a CU-04.

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

**TipoClaveApi**  *«enum»*

Produccion, Prueba

# **Envíos, bultos y ciclo de vida**

*Es el agregado central del dominio. Usa la configuración vigente (tarifario y reglas) en el momento del alta del envío.*

## **Clases**

**Envio**

*raíz del agregado del envío. Su operadorId y su comercioId coinciden con los de su RelacionComercial, y el envío los copia a sus entidades hijas (Bulto, EventoEnvio, IntentoEntrega). Cuando el que trabaja es personal del operador, la sesión no trae comercioId y el interceptor no lo puede asignar (ADR-0002, sección 2.3)*

**Atributos**

* id: Guid

* relacionComercialId: Guid

* referenciaComercio: string (identificador propio del comercio; único dentro de una misma RelacionComercial — sostiene la idempotencia de la importación masiva, RF 7\)

* numero: string (único por operador: índice único {operadorId, numero} — ADR-0002; CU-10)

* tokenSeguimiento: string (para el enlace público de seguimiento; nunca se expone el id interno — RF 25\. Es un token firmado que incluye el operadorId, no un valor aleatorio — ADR-0002, sección 2.5; CU-10 y CU-60)

* destinatario: Destinatario

* direccion: Direccion

* zonaId: Guid

* franjaHorariaId: Guid (opcional)

* fechaEntregaProgramada: DateOnly (opcional; la fija la reprogramación — CU-19 y CU-61 — y la usan el armado de rutas — CU-40 — y la ventana estimada del seguimiento — CU-60)

* modalidad: Modalidad

* versionTarifarioId: Guid

* versionReglasId: Guid

* montoTarifa: decimal

* estado: EstadoEnvio

* creadoEn: DateTimeOffset

* esPrueba: bool (creado con una clave de prueba de la API pública; nunca llega a la operación real)

**Métodos**

* transicionar(nuevoEstado: EstadoEnvio, origen: OrigenEvento, responsableId: Guid (opcional)): void — único punto que cambia el estado (RF 11\) y genera el EventoEnvio (RF 12\). El responsable es opcional porque las transiciones del Sistema y del seguimiento público no tienen un usuario

* reprogramar(fecha: DateOnly, franjaHorariaId: Guid (opcional), origen: OrigenEvento, responsableId: Guid (opcional)): void — cambia la fecha y, cuando corresponde, aplica la transición (T9, T18 o T19). Si el envío está Admitido (CU-61) o ya Reprogramado (CU-19), sólo cambia la fecha, sin transición

* agregarBulto(bulto: Bulto): void

**Bulto**

**Atributos**

* id: Guid

* envioId: Guid

* codigo: string (lo que se escanea — CU-30 y CU-51; único por operador)

* montoTarifa: decimal (cada bulto se tarifa por separado y el envío suma sus bultos — CU-10)

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

* motivoNoEntregaId: Guid (obligatorio si resultado \= Fallido — invariante de dominio; el motivo pertenece a la VersionReglas del envío — sección 6.6 de la letra)

* evidencia: PruebaEntrega (opcional)

* observaciones: string (opcional)

**PruebaEntrega**

*sin id propio — evidencia de un intento, exitoso o fallido (RF 20 y RF 21\)*

**Atributos**

* firmaArchivoId: Guid (opcional; obligatoria en un intento exitoso: la firma del receptor es siempre obligatoria — CU-17; T6)

* fotoArchivoId: Guid (opcional; aplica a intento exitoso o fallido)

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

* nombreReceptor: string (opcional; quién recibió por el comercio — CU-31)

* documentoReceptor: string (opcional)

* entregadaAlComercioEn: DateTimeOffset (opcional)

**Métodos**

* marcarRecibida(): void

* cerrar(nombreReceptor: string, documentoReceptor: string): void — el comercio recibió el envío devuelto (T15, CU-31)

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

* operadorId: Guid

* repartidorId: Guid

* vehiculoId: Guid

* fecha: DateOnly

* estado: EstadoRuta

* creadaEn: DateTimeOffset

* despachadaEn: DateTimeOffset (opcional)

* revision: long (inicia en 1 y aumenta con cada modificación efectiva, incluidas sus paradas; token de concurrencia según la especificación técnica)

* reservaActiva: bool (mantenida por el agregado: Planificada con paradas, Despachada o EnCurso; no editable desde una petición)

**Métodos**

* agregarParada(envioId: Guid): void

* modificarPlanificacion(fecha: DateOnly, repartidorId: Guid, vehiculoId: Guid): void — sólo en estado Planificada (ampliación de CU-40 acordada el 09/10/2026). La aplicación coordina la revalidación completa de los envíos, los recursos activos y su disponibilidad, y el cambio atómico de reservas; si falla, se conservan los datos anteriores. No modifica las paradas ni repite las transiciones de asignación de los envíos.

* quitarParada(envioId: Guid): void — sólo si la ruta no fue despachada; la parada se elimina (T4, T19)

* En el cambio programado de franjas de CU-04, un envío AsignadoARuta afectado sale de su ruta Planificada por T19 y queda Reprogramado con la nueva franja, conservando el día de entrega. Si sólo lo determinaba la ruta, ese día se guarda como fechaEntregaProgramada al reprogramar. La parada se elimina sin borrar la evidencia histórica; si la ruta queda vacía, libera sus recursos. Se muestran los envíos afectados antes de confirmar y se dejan disponibles para reasignación manual mediante T13 (acordado al definir CU-40 el 09/10/2026).

* ordenarParadas(): void — por hora de inicio de la franja comprometida y, dentro de la misma franja, por zona y código postal (CU-42)

* moverParada(paradaId: Guid, nuevoOrden: int): void — ajuste manual del despachador (CU-42)

* despachar(): void

**Parada**

**Atributos**

* id: Guid

* rutaId: Guid

* envioId: Guid

* orden: int

* estado: EstadoParada

* llegadaEn: DateTimeOffset (opcional)

**ValidacionRuta**

*entidad histórica de Planificación, vinculada a Ruta; inmutable una vez confirmada, acordada el 09/10/2026*

**Atributos**

* id: Guid

* rutaId: Guid

* revisionRuta: long (revisión resultante de la confirmación evaluada)

* validadaEn: DateTimeOffset

* responsableId: Guid (opcional, según el origen de la operación)

* operacion: OperacionValidacionRuta

* fechaRuta: DateOnly (copia de la fecha evaluada)

* repartidorId: Guid (copia de la asignación evaluada)

* vehiculoId: Guid

* capacidadPesoKg: decimal (copia del límite utilizado)

* capacidadVolumenM3: decimal (copia del límite utilizado)

* largoCargaCm, anchoCargaCm, altoCargaCm: decimal (copias de las dimensiones utilizadas)

* versionReglasId: Guid

* maxParadasPorRuta: int (copia del límite utilizado)

* bultos: DatosBultoValidado\[\]

**DatosBultoValidado**

*objeto valor de ValidacionRuta; no depende de la existencia de una Parada*

* envioId: Guid

* bultoId: Guid

* pesoKg: decimal

* largoCm, anchoCm, altoCm: decimal

* volumenM3: decimal (calculado a partir de las dimensiones conservadas)

Una confirmación exitosa crea un nuevo registro con todos los bultos evaluados. Se guarda con la operación en la misma transacción. Eliminar una parada no elimina esta evidencia, ni una nueva confirmación sobrescribe las anteriores. Las referencias a envíos y bultos no implican acceso a las entidades internas de Envíos.

## **Enumerados**

**EstadoRuta**  *«enum»*

Planificada, Despachada, EnCurso, Finalizada

*sin Cancelada: ningún caso de uso cancela una ruta. Una ruta no despachada se vacía quitando sus paradas*

**EstadoParada**  *«enum»*

Pendiente, Completada, Fallida

**OperacionValidacionRuta**  *«enum»*

Creacion, Agregado, Modificacion, Despacho, CambioFranjaProgramado

## **Notas**

* RF 15 ("un envío no puede estar en dos rutas a la vez") es una invariante de aplicación/base de datos, no una cardinalidad del diagrama: un envío no puede tener más de una parada activa (estado distinto de Completada o Fallida) al mismo tiempo. Se valida en la aplicación y se refuerza con un índice único parcial de PostgreSQL: UNIQUE (envioId) WHERE estado = 'Pendiente'. Al quitar un envío de una ruta no despachada (CU-41, T4, T19), la parada se elimina.

* Creación y reserva de recursos (acordado para CU-40 el 09/10/2026): la ruta se persiste al confirmar con al menos un envío. Una ruta Planificada con paradas reserva repartidor y vehículo para su fecha; si queda vacía por CU-41, conserva su estado y libera la reserva. Volver a agregar envíos requiere comprobar y reservar nuevamente los recursos en la misma transacción. Una ruta Despachada o EnCurso mantiene la ocupación para su fecha y bloquea nuevas asignaciones si sigue sin finalizar desde una fecha anterior. Completar o fallar paradas no las elimina ni libera por sí solo los recursos: la ruta debe pasar a Finalizada. Una ruta Finalizada no reserva recursos; la excepción de ruta vacía sólo corresponde a Planificada.

* El orden de las paradas (RF 16) usa un criterio fijo, sin ADR de despacho: hora de inicio de la franja comprometida y, dentro de la misma franja, zona y código postal. El despachador puede ajustarlo a mano (CU-42). Si el equipo aborda el opcional de optimización (sección 7.3 de la letra), se compara contra este criterio.

* Evidencia de carga (acordado para CU-40 el 09/10/2026): ValidacionRuta conserva los pesos y dimensiones usados por bulto, los recursos y límites evaluados y el momento/responsable/operación de cada confirmación. Al agregar envíos, modificar la planificación o despachar (CU-43), se revalida la carga completa con las medidas actuales de recepción y el respaldo declarado por valor no medido. Las confirmaciones posteriores no sobrescriben la evidencia anterior. La evidencia se confirma en la misma transacción que la operación y pertenece a la ruta, sin eliminarse al quitar una parada por CU-41. Las tablas, restricciones y consultas del historial se definen en la especificación técnica de CU-40; su implementación queda pendiente.

* La validación de restricciones de RF 14 (máximo de paradas, capacidad de peso/volumen vía Vehiculo.admiteCarga, compatibilidad de franja horaria) no vive en una sola clase: la ejecuta un servicio de dominio (por ejemplo PlanificadorDeRuta) que orquesta Ruta, Vehiculo y los Envio candidatos. No es una clase de este diagrama; se documenta en la arquitectura.

* Reglas de planificación (acordado para CU-40 el 09/10/2026): crear, agregar envíos, modificar la planificación y despachar se validan con la versión de reglas vigente al confirmar. Se conserva su identificador y el máximo de paradas aplicado junto con la evidencia de cada confirmación. La prevalidación no congela la versión; un cambio que invalida la selección impide confirmar y se informa el nuevo límite. Las reglas posteriores no modifican retroactivamente una ruta Despachada o EnCurso ni la evidencia de su despacho. Esta versión de planificación no reemplaza la versión de reglas propia de cada envío.

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

* declarada: bool (el repartidor declaró que devuelve el bulto — CU-54)

* recibida: bool (el operario confirmó que llegó — CU-55)

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

* tiposEvento: TipoEventoAviso\[\] (una suscripción puede recibir varios tipos de evento — CU-63)

* secretoFirma: string (para que el comercio verifique el origen del mensaje; se guarda cifrado y se muestra una sola vez)

* activa: bool

**Métodos**

* desactivar(): void

* regenerarSecreto(): void — invalida el secreto anterior (CU-63, A3)

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

Email

*sólo se implementa correo, por SMTP (Mailpit en desarrollo, Brevo en producción). SMS y WhatsApp quedan fuera de alcance*

**TipoNotificacion**  *«enum»*

CambioDeEstado, VentanaEstimada, SolicitudDeReprogramacion

**EstadoNotificacion**  *«enum»*

Pendiente, Enviada, Fallida, NoEnviable

*NoEnviable: el destinatario no tiene correo; se registra sin reintentos (CU-62, A2)*

**TipoEventoAviso**  *«enum»*

CambioDeEstado, EntregaFallida, Devolucion

**EstadoEntregaAviso**  *«enum»*

Pendiente, Exitosa, Fallida, EnColaDeFallidos

## **Coordinación entre agregados (Envio, Devolucion, Incidencia, Notificacion, EntregaAviso)**

Los agregados no se llaman entre sí directamente: la capa de aplicación (no el dominio) coordina. Dentro de un mismo módulo, las reacciones se guardan en la misma transacción; entre módulos o hacia el Worker, se usa el Outbox (ADR-0001, sección 2.5; ADR-0003).

| Hecho | Reacción | Mecanismo |
| :---- | :---- | :---- |
| Se inicia la devolución (T10, T12 o T14) | Se crea la Devolucion en estado Pendiente | Misma transacción (CU-20) |
| El comercio recibe el envío devuelto (T15) | La Devolucion pasa a Cerrada y el envío a Devuelto | Misma transacción (CU-31) |
| Se declara un extravío (T16) | Se crea una Incidencia de tipo Extravío | Misma transacción (CU-21) |
| Cualquier cambio de estado del envío | Notificacion al destinatario y EntregaAviso a los comercios suscritos | Outbox y Worker (ADR-0003; CU-62 y CU-64) |
| El envío queda Entregado o Devuelto | Depósito registra el envío como liquidable | Outbox (CU-32) |
| Se registra una entrega, un intento fallido o un extravío | Planificación marca la parada como completada o fallida | Outbox (CU-17, CU-18 y CU-21) |

# **Depósito, devoluciones y liquidaciones/reportes**

*Los reportes de RF 30 (cumplimiento por zona/repartidor/comercio, motivos de no entrega, volumen por período, liquidación por comercio) no son clases nuevas: son consultas sobre datos de EventoEnvio, IntentoEntrega, Envio y Liquidacion, y se resuelven como proyecciones o vistas de solo lectura en la capa de arquitectura, no en el modelo de dominio.*

## **Clases**

**RecepcionDeposito**

*RF 10 — recepción del envío en depósito, verificación contra lo declarado por el comercio*

**Atributos**

* id: Guid

* envioId: Guid (para saber qué bultos de un envío ya llegaron sin consultar las tablas de Envíos — CU-30)

* bultoId: Guid (único: un bulto se recibe una sola vez — CU-30, A2)

* recibidoEn: DateTimeOffset

* resultado: ResultadoRecepcion (ConDiscrepancia si alguna medida difiere de lo declarado en más de la tolerancia)

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

* emitir(): void — Borrador → Emitida (CU-32)

* marcarPagada(): void — Emitida → Pagada (CU-32, A3)

**LineaLiquidacion**

**Atributos**

* id: Guid

* liquidacionId: Guid

* envioId: Guid (único: un envío aparece en una sola liquidación — CU-32, A2)

* concepto: string

* monto: decimal

## **Enumerados**

**ResultadoRecepcion**  *«enum»*

Conforme, ConDiscrepancia

**EstadoLiquidacion**  *«enum»*

Borrador, Emitida, Pagada

## **Notas**

* Devolucion ya está completamente definida en la sección de Envíos, bultos y ciclo de vida, junto al resto del ciclo de vida del envío, y no se repite acá.

* Depósito y liquidaciones mantiene su propia copia de los envíos liquidables, alimentada por los eventos EnvioEntregado y EnvioDevuelto; no consulta las tablas de Envíos (CU-32).

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
| Envio | 0..\* | asociación | 0..1 | FranjaHoraria |
| RelacionComercial | 1 | asociación | 0..\* | ClaveApi |
| Envio | 0..\* | asociación | 1 | VersionTarifario |
| Envio | 0..\* | asociación | 1 | VersionReglas |
| EventoEnvio | 0..\* | asociación | 0..1 | Usuario |
| IntentoEntrega | 0..\* | asociación | 0..1 | MotivoNoEntrega |
| Ruta | 1 | composición | 0..\* | Parada |
| Ruta | 1 | composición | 1..\* | ValidacionRuta |
| ValidacionRuta | 1 | composición | 0..\* | DatosBultoValidado |
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

**Resueltos**

* Cobertura de Zona: lista de códigos postales (codigosPostales: string\[\]), por simplicidad; cada código postal pertenece a una sola zona activa del operador (CU-04). Se descartó el polígono geográfico.

* Comercio ↔ Operador: relación muchos a muchos a través de RelacionComercial, según la letra ("un mismo comercio puede operar con más de un operador logístico").

* TipoIncidencia: enum fijo del sistema. La letra no exige que sea configurable por operador, a diferencia de MotivoNoEntrega (CU-22).

* Orden de las paradas (RF 16): criterio fijo, sin ADR de despacho, y ajuste manual del despachador (CU-42). Se eliminó el enum CriterioOrden.

* RF 15 (un envío sin dos paradas activas simultáneas): validación en la aplicación e índice único parcial de PostgreSQL (sección de Planificación).

* Coordinación entre agregados: misma transacción dentro de un módulo y Outbox entre módulos o hacia el Worker (ADR-0001, sección 2.5; ADR-0003).

* RelacionComercial con filtro de inquilino: implementa IOperadorOwned e IComercioOwned (ADR-0002 v2.1, sección 2.6).

* Usuario y ClaveApi frente al filtro de inquilino: Usuario (Identity) no tiene filtro y la búsqueda de ClaveApi por hash es el único acceso que lo ignora (ADR-0002 v2.1, secciones 2.4 y 4).

**Abiertos**

* Tolerancia de la recepción en depósito: la implementación de CU-30 registra una discrepancia cuando el peso o una dimensión medida difiere más de un 5 % de lo declarado (`RecepcionDeposito.Tolerancia`). Falta confirmar el valor con el equipo y decidir si pasa a ser una regla operativa configurable (VersionReglas).

* Correo único en Identity: por defecto el correo es único en toda la tabla, no por operador, así que una persona que trabaje para dos operadores necesita dos correos (ADR-0002 v2.1, sección 5).
