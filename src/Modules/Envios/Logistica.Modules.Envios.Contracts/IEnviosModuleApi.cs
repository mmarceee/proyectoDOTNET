using Logistica.Modules.Envios.Contracts.Results;

namespace Logistica.Modules.Envios.Contracts;

// Lo que el módulo Envíos ofrece a los demás módulos, en memoria y en la misma transacción
// (ADR-0001, addendum 3). Los otros módulos nunca tocan las tablas ni las entidades de Envíos.
public interface IEnviosModuleApi
{
    Task<IReadOnlyList<CustodiaBulto>> ConsultarCustodiaAsync(IReadOnlyCollection<Guid> envioIds, CancellationToken ct);
    Task ConfirmarCustodiaAsync(ConciliacionBultos confirmacion, CancellationToken ct);
    Task RegistrarCustodiaCargaAsync(Guid rutaId, IReadOnlyCollection<Guid> bultoIds, Guid operacionId,
        Guid? actor, DateTimeOffset ocurridoEn, CancellationToken ct);
    Task RegistrarCustodiaVisitaAsync(Guid rutaId, IReadOnlyCollection<Guid> entregados, Guid operacionId,
        Guid? actor, DateTimeOffset ocurridoEn, CancellationToken ct);
    Task ReintegrarBultosAsync(Guid rutaId, IReadOnlyCollection<Guid> bultoIds, Guid? actor, CancellationToken ct);
    // CU-51: lectura y T5 dentro de la transacción compartida de confirmación de carga.
    Task<IReadOnlyList<EnvioParaCarga>> ConsultarCargaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<BultoParaCarga>> ReconocerBultosAsync(IReadOnlyCollection<string> codigos, CancellationToken ct);
    Task<bool> RegistrarCargaAsync(IReadOnlyCollection<Guid> ids, Guid? responsable, DateTimeOffset ocurridoEn, CancellationToken ct);
    Task<bool> RegistrarCargaPorBultosAsync(IReadOnlyCollection<Guid> bultoIds, Guid? responsable, DateTimeOffset ocurridoEn, CancellationToken ct);

    // null si el código no es de un bulto del operador actual (CU-30, A1).
    Task<EnvioParaRecepcion?> ObtenerParaRecepcionAsync(string codigoBulto, CancellationToken ct);

    // Aplica T2 a un bulto (RF 31): con el primero el envío queda En operación. Devuelve true cuando ya llegaron todos
    // los bultos y el envío quedó EnDeposito. Rechaza bultos que no estén Admitidos y envíos cancelados.
    Task<bool> RecibirBultoAsync(Guid envioId, Guid bultoId, Guid? responsableId, CancellationToken ct, Guid? operacionId = null);
    Task<IReadOnlyList<EnvioPlanificable>> ObtenerParaPlanificacionAsync(IReadOnlyCollection<Guid>? envioIds, CancellationToken ct);
    Task AsignarARutaAsync(Guid rutaId, IReadOnlyList<AsignacionEnvioRuta> asignaciones, Guid? responsableId, CancellationToken ct);
    // T4. Planificación comprueba pertenencia y estado de la ruta dentro de la transacción compartida.
    Task DesasignarDeRutaAsync(Guid rutaId, Guid envioId, Guid? responsableId, CancellationToken ct);

    // CU-17 y CU-18 por bulto (RF 31 y 32), desde la sincronización de la PWA (CU-52; ADR-0004, sección 2.11). Ejecución
    // consulta los bultos del envío para clasificar cada uno antes de registrar la visita. El registro no lanza por
    // reglas de negocio: devuelve el resultado para que Ejecución aplique la política de conflictos. Guarda con la unidad
    // de trabajo en curso, junto con el registro de la operación.
    Task<EnvioParaVisita?> ConsultarParaVisitaAsync(Guid envioId, CancellationToken ct, Guid? rutaId = null);
    Task<RegistroEnCalle> RegistrarVisitaAsync(VisitaARegistrar visita, CancellationToken ct);

    // CU-04: ¿algún envío sin terminar tiene una de estas franjas comprometida en esa fecha o después? Administración
    // lo pregunta antes de reemplazar una franja, para no cambiar un horario prometido sin aviso.
    Task<bool> HayEnviosComprometidosAsync(IReadOnlyCollection<Guid> franjaIds, DateOnly desde, CancellationToken ct);

    // CU-04: los envíos sin terminar con una de estas franjas pero sin fecha propia. Su día lo da la ruta en la que
    // estén: Administración se los pasa a Planificación para saber si alguna ruta desde la fecha del cambio los tiene.
    Task<IReadOnlyList<Guid>> EnviosSinFechaConFranjasAsync(IReadOnlyCollection<Guid> franjaIds, CancellationToken ct);

    // CU-70, paso 2 (T14): inicia la devolución de los envíos Reprogramado cuyo plazo de devolución venció. "hoy" es el
    // día del operador que se cierra. Devuelve cuántos devolvió. Cada envío se guarda en su propia transacción.
    Task<int> DevolverReprogramadosVencidosAsync(DateOnly hoy, CancellationToken ct);

    // CU-60: lo que el seguimiento público muestra de un envío. null si no existe o es de otro operador: el inquilino
    // sale del token firmado del enlace, y el filtro "Tenant" hace el resto.
    Task<SeguimientoEnvio?> SeguimientoDeAsync(Guid envioId, CancellationToken ct);

    // CU-60: el Id de un envío del operador a partir de su número (ENV-000001), para armar su enlace de seguimiento desde
    // el Backoffice. null si no existe o es de otro operador.
    Task<Guid?> IdPorNumeroAsync(string numero, CancellationToken ct);

    // CU-60: los envíos del operador, del más nuevo al más viejo, para elegir desde el Backoffice cuál enlace de
    // seguimiento mandar. texto busca por número o destinatario, igual que el listado de envíos (CU-13).
    Task<PaginaEnviosParaSeguimiento> ListarParaSeguimientoAsync(string? texto, int pagina, CancellationToken ct);

    // CU-55 (T8): los envíos que salieron y no llegaron a intentarse vuelven al depósito, y cada uno publica
    // EnvioReintegradoADeposito por Outbox. Si alguno ya no está EnTransito, lanza una DomainException.
    Task ReintegrarADepositoAsync(IReadOnlyCollection<Guid> envioIds, Guid? responsableId, CancellationToken ct);
    // CU-55 (T4): los envíos de la ruta que no salieron, porque no se cargaron o se cargaron incompletos, quedan otra vez
    // en el depósito al cerrar la rendición, y cada uno publica EnvioDesasignadoDeRuta. Se omiten los que ya no están
    // AsignadoARuta (el despachador pudo quitarlos antes). El coordinador confirma antes su ubicación física
    // y comprueba la reserva de ruta bajo el bloqueo del operador. Devuelve cuántos volvieron.
    Task<int> ReintegrarNoCargadosAsync(Guid rutaId, IReadOnlyCollection<Guid> envioIds, CancellationToken ct);

    // CU-54 y CU-55 (RF 31): los envíos con todos sus bultos y el estado de cada uno, para armar y confirmar la rendición.
    // Los que no son del operador actual no vuelven.
    Task<IReadOnlyList<EnvioParaRendicion>> ConsultarParaRendicionAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    // CU-55, A1: los bultos que tenían que volver en una rendición y no llegaron. Abre una incidencia por envío con sus
    // códigos; el despachador decide si declara el extravío (CU-21).
    Task RegistrarFaltantesAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct);

    // CU-55: los envíos en devolución cuyos bultos volvieron en la rendición quedan con la devolución recibida en el
    // depósito, lista para entregar al comercio (CU-31). Los envíos sin devolución se ignoran.
    Task RegistrarDevolucionesRecibidasAsync(IReadOnlyCollection<Guid> envioIds, CancellationToken ct);

    // CU-31: la devolución de un envío, buscada por su número o por el código de uno de sus bultos. null si no existe,
    // es de otro operador o el envío no tiene una devolución.
    Task<DevolucionParaEntrega?> ConsultarDevolucionAsync(string numeroOCodigo, CancellationToken ct);

    // CU-31 (T15): cierra la devolución con quién la recibió, pasa el envío a Devuelto, abre una incidencia por los
    // bultos que no se entregaron (A2) y publica EnvioDevuelto por Outbox. RF 31: si vuelven bultos sueltos mientras otros
    // del envío siguen su camino, sólo esos bultos pasan a Devuelto y el envío no cambia. DomainException si el envío no está en
    // devolución o todavía no volvió al depósito (A1).
    Task ConfirmarDevolucionAsync(DevolucionAConfirmar confirmacion, CancellationToken ct);

    // CU-66: los envíos de las rutas en la calle (para su avance) y los que esperan la entrega con fecha hasta "hasta"
    // inclusive (para el riesgo). El operador lo pone el filtro "Tenant".
    Task<IReadOnlyList<EnvioParaTablero>> ListarParaTableroAsync(IReadOnlyCollection<Guid> deRutas, DateOnly hasta,
        CancellationToken ct);

    // CU-61, paso 2: las fechas y franjas que el destinatario puede elegir. null si el envío no existe o es de otro
    // operador (el inquilino sale del token del enlace); vacía si ya no se puede reprogramar.
    Task<IReadOnlyList<OpcionReprogramacion>?> OpcionesReprogramacionAsync(Guid envioId, CancellationToken ct);

    // CU-61, pasos 3 a 5 (T18, T19 o nuevo intento). false si el envío no existe o es de otro operador; una regla rota
    // (A1, A2, A3) lanza DomainException.
    Task<bool> ReprogramarPorDestinatarioAsync(Guid envioId, DateOnly fecha, Guid franjaId, CancellationToken ct);

    // CU-61, verificación previa: el correo y el teléfono del destinatario. null si el envío no existe o es de otro
    // operador (el inquilino sale del token del enlace).
    Task<ContactoDestinatario?> ContactoDestinatarioAsync(Guid envioId, CancellationToken ct);

    // CU-32: los envíos del comercio que terminaron entre desde (inclusive) y hasta (exclusive) con algún bulto entregado
    // o devuelto, con la tarifa con que se admitieron (D8). Si todos sus bultos quedaron cancelados o extraviados, no se
    // cobran. El operador lo pone el filtro "Tenant".
    Task<IReadOnlyList<EnvioLiquidable>> ListarLiquidablesAsync(Guid comercioId, DateTimeOffset desde, DateTimeOffset hasta,
        CancellationToken ct);

    // RF 33 y CU-32: los recargos de nueva visita del comercio registrados entre desde (inclusive) y hasta (exclusive).
    // Cada uno se cobra una vez; el RecargoId sirve para no repetirlo en otra liquidación. El operador lo pone el filtro
    // "Tenant".
    Task<IReadOnlyList<RecargoLiquidable>> ListarRecargosAsync(Guid comercioId, DateTimeOffset desde, DateTimeOffset hasta,
        CancellationToken ct);
}
