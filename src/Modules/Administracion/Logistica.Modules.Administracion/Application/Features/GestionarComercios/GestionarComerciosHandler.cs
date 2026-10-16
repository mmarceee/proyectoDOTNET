using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.Modules.Envios.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.GestionarComercios;

internal sealed record ComercioEncontrado(Guid Id, string RazonSocial, bool Vinculado);

internal sealed class GestionarComerciosHandler(IComerciosRepository comercios, IEnviosDeComercio envios,
    ICurrentTenant tenant, IUnidadDeTrabajo unidad, TimeProvider reloj) : IRelacionComercialActiva
{
    public Task<IReadOnlyList<ComercioVista>> ListarAsync(string? buscar, EstadoRelacion? estado, CancellationToken ct)
        => comercios.ListarAsync(buscar?.Trim(), estado, ct);

    public Task<ComercioVista?> ObtenerAsync(Guid id, CancellationToken ct) => comercios.ObtenerAsync(id, ct);

    public static string ValidarDocumento(string documento)
    {
        documento = documento?.Trim() ?? "";
        if (documento.Length != 12 || documento.Any(c => c < '0' || c > '9'))
            throw new DomainException("Ingresá el documento fiscal con sus 12 dígitos, sin puntos ni guiones.");
        return documento;
    }

    public async Task<ComercioEncontrado?> BuscarAsync(string documento, CancellationToken ct)
    {
        var comercio = await comercios.BuscarDocumentoAsync(ValidarDocumento(documento), ct);
        return comercio is null ? null : new(comercio.Id, comercio.RazonSocial,
            await comercios.ObtenerAsync(comercio.Id, ct) is not null);
    }

    public async Task<Guid> CrearAsync(string documento, string razonSocial, string email, CancellationToken ct)
    {
        var operador = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene operador.");
        documento = ValidarDocumento(documento);
        Guid resultado = default;
        await unidad.EjecutarAsync(async () =>
        {
            var comercio = await comercios.BuscarDocumentoAsync(documento, ct);
            if (comercio is not null && await comercios.ObtenerAsync(comercio.Id, ct) is not null)
                throw new DomainException("Este comercio ya está vinculado al operador. Abrilo desde el listado.");
            if (comercio is null)
            {
                comercio = Comercio.Crear(razonSocial?.Trim() ?? "", documento);
                comercios.Agregar(comercio);
            }
            comercios.Agregar(RelacionComercial.Crear(operador, comercio.Id, email, reloj.GetUtcNow()));
            await comercios.GuardarAsync(ct);
            resultado = comercio.Id;
        }, ct);
        return resultado;
    }

    public Task ModificarAsync(Guid id, uint version, string accion, string email, CancellationToken ct)
        => unidad.EjecutarAsync(async () =>
        {
            var relacion = await comercios.BloquearRelacionAsync(id, ct)
                ?? throw new KeyNotFoundException();
            if (relacion.Version != version)
                throw new DomainException("Otro administrador modificó la relación. Recargá la página antes de confirmar.");
            switch (accion)
            {
                case "contacto": relacion.ModificarContacto(email); break;
                case "suspender": relacion.Suspender(); break;
                case "reactivar": relacion.Reactivar(); break;
                case "baja": relacion.DarDeBaja(await envios.ContarPendientesAsync(id, ct)); break;
                default: throw new DomainException("La acción no es válida.");
            }
            await comercios.GuardarAsync(ct);
        }, ct);

    public async Task ExigirAsync(Guid comercioId, CancellationToken ct)
    {
        var relacion = await comercios.BloquearRelacionAsync(comercioId, ct);
        if (relacion?.Estado != EstadoRelacion.Activa)
            throw new DomainException("Tu comercio no tiene una relación activa con este operador. No se pueden crear nuevos envíos.");
    }
}
