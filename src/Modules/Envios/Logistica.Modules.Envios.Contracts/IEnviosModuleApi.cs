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
}
