using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.Modules.Administracion.Domain.Tarifas;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;

// CU-05, pasos 3 a 6: crear el borrador (A4: si ya hay uno, se abre ése) y editar sus reglas y ajustes. Coordina: las
// reglas las validan VersionTarifario, ReglaTarifa y AjusteTarifa. Las acciones devuelven false si no hay borrador,
// por ejemplo porque otro administrador lo publicó mientras tanto.
internal sealed class GestionarBorradorHandler(ITarifarioRepository versiones, IZonaRepository zonas, ICurrentTenant tenant)
{
    // A4: hay a lo sumo un borrador por operador. Si ya existe, se sigue con ése; si no, se copia la última publicada.
    public async Task NuevaVersionAsync(CancellationToken ct)
    {
        if (await versiones.ObtenerBorradorAsync(ct) is not null)
        {
            return;
        }

        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var borrador = VersionTarifario.CrearBorrador(operadorId, await versiones.SiguienteNumeroAsync(ct),
            await versiones.UltimaPublicadaAsync(ct));
        versiones.Agregar(borrador);
        await versiones.GuardarCambiosAsync(ct);
    }

    public async Task<bool> AgregarReglaAsync(DatosReglaTarifa datos, CancellationToken ct)
    {
        // La zona tiene que ser del operador y estar activa: el filtro oculta las de otro operador.
        if (await zonas.ObtenerAsync(datos.ZonaId, ct) is not { Activa: true })
        {
            throw new DomainException("Elegí una zona activa del operador.");
        }

        return await EditarAsync(borrador => borrador.AgregarRegla(datos), ct);
    }

    public Task<bool> QuitarReglaAsync(Guid reglaId, CancellationToken ct)
    {
        return EditarAsync(borrador => borrador.QuitarRegla(reglaId), ct);
    }

    public Task<bool> AgregarAjusteAsync(DatosAjusteTarifa datos, CancellationToken ct)
    {
        return EditarAsync(borrador => borrador.AgregarAjuste(datos), ct);
    }

    public Task<bool> QuitarAjusteAsync(Guid ajusteId, CancellationToken ct)
    {
        return EditarAsync(borrador => borrador.QuitarAjuste(ajusteId), ct);
    }

    private async Task<bool> EditarAsync(Action<VersionTarifario> cambio, CancellationToken ct)
    {
        if (await versiones.ObtenerBorradorAsync(ct) is not VersionTarifario borrador)
        {
            return false;
        }

        cambio(borrador);
        await versiones.GuardarCambiosAsync(ct);
        return true;
    }
}
