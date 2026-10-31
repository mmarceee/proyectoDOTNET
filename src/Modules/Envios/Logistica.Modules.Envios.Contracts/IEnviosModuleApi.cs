using Logistica.Modules.Envios.Contracts.Results;

namespace Logistica.Modules.Envios.Contracts;

// Lo que el módulo Envíos ofrece a los demás módulos, en memoria y en la misma transacción
// (ADR-0001, addendum 3). Los otros módulos nunca tocan las tablas ni las entidades de Envíos.
public interface IEnviosModuleApi
{
    // CU-51: lectura y T5 dentro de la transacción compartida de confirmación de carga.
    Task<IReadOnlyList<EnvioParaCarga>> ConsultarCargaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<BultoParaCarga>> ReconocerBultosAsync(IReadOnlyCollection<string> codigos, CancellationToken ct);
    Task<bool> RegistrarCargaAsync(IReadOnlyCollection<Guid> ids, Guid? responsable, DateTimeOffset ocurridoEn, CancellationToken ct);

    // null si el código no es de un bulto del operador actual (CU-30, A1).
    Task<EnvioParaRecepcion?> ObtenerParaRecepcionAsync(string codigoBulto, CancellationToken ct);

    // Aplica T2: Admitido → EnDeposito. Lanza una DomainException si el envío no está Admitido.
    Task RecibirEnDepositoAsync(Guid envioId, Guid? responsableId, CancellationToken ct);
    Task<IReadOnlyList<EnvioPlanificable>> ObtenerParaPlanificacionAsync(IReadOnlyCollection<Guid>? envioIds, CancellationToken ct);
    Task AsignarARutaAsync(Guid rutaId, IReadOnlyList<AsignacionEnvioRuta> asignaciones, Guid? responsableId, CancellationToken ct);
    // T4. Planificación comprueba pertenencia y estado de la ruta dentro de la transacción compartida.
    Task DesasignarDeRutaAsync(Guid rutaId, Guid envioId, Guid? responsableId, CancellationToken ct);

    // CU-17 (T6) y CU-18 (T7 con T9 o T10), desde la sincronización de la PWA (CU-52). No lanzan por reglas de negocio:
    // devuelven el resultado para que Ejecución aplique la política de conflictos (ADR-0004). Guardan con la unidad de
    // trabajo en curso, junto con el registro de la operación.
    Task<RegistroEnCalle> RegistrarEntregaAsync(EntregaARegistrar entrega, CancellationToken ct);
    Task<RegistroEnCalle> RegistrarIntentoFallidoAsync(IntentoFallidoARegistrar intento, CancellationToken ct);

    // CU-04: ¿algún envío sin terminar tiene una de estas franjas comprometida en esa fecha o después? Administración
    // lo pregunta antes de reemplazar una franja, para no cambiar un horario prometido sin aviso.
    Task<bool> HayEnviosComprometidosAsync(IReadOnlyCollection<Guid> franjaIds, DateOnly desde, CancellationToken ct);

    // CU-04: los envíos sin terminar con una de estas franjas pero sin fecha propia. Su día lo da la ruta en la que
    // estén: Administración se los pasa a Planificación para saber si alguna ruta desde la fecha del cambio los tiene.
    Task<IReadOnlyList<Guid>> EnviosSinFechaConFranjasAsync(IReadOnlyCollection<Guid> franjaIds, CancellationToken ct);

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
    // bultos que no se entregaron (A2) y publica EnvioDevuelto por Outbox. DomainException si el envío no está en
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
}
