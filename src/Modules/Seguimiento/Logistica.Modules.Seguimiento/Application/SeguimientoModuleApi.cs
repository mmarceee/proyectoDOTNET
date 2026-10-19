using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Logistica.Modules.Seguimiento.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application;

// Implementación del contrato de Seguimiento. Es internal: los otros módulos sólo ven la interfaz.
internal sealed class SeguimientoModuleApi(TokenSeguimiento tokens, ICurrentTenant tenant) : ISeguimientoModuleApi
{
    // El operador sale de la sesión, nunca del llamador: no se puede pedir el enlace de un envío de otro operador.
    public string EnlaceDe(Guid envioId)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        return $"/seguimiento/{tokens.Generar(operadorId, envioId)}";
    }
}
