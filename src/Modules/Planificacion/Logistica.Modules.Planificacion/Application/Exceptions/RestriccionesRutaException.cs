using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Application.Exceptions;

internal sealed class RestriccionesRutaException(IReadOnlyList<Domain.Rutas.RestriccionRuta> restricciones)
    : DomainException(string.Join(" ", restricciones.Select(r => r.Mensaje)))
{
    public IReadOnlyList<Domain.Rutas.RestriccionRuta> Restricciones { get; } = restricciones;
}
