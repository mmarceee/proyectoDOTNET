using Logistica.Modules.Envios.Contracts.Results;

namespace Logistica.Modules.Envios.Contracts;

// Lo que el módulo Envíos ofrece a los demás módulos, en memoria y en la misma transacción
// (ADR-0001, addendum 3). Los otros módulos nunca tocan las tablas ni las entidades de Envíos.
public interface IEnviosModuleApi
{
    // null si el código no es de un bulto del operador actual (CU-30, A1).
    Task<EnvioParaRecepcion?> ObtenerParaRecepcionAsync(string codigoBulto, CancellationToken ct);

    // Aplica T2: Admitido → EnDeposito. Lanza una DomainException si el envío no está Admitido.
    Task RecibirEnDepositoAsync(Guid envioId, Guid? responsableId, CancellationToken ct);
}
