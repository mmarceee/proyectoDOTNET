using Logistica.Modules.Administracion.Domain.Reglas;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.ConfigurarReglas;

// CU-06, pasos 3 y 4: crear el borrador copiando la última versión publicada (si ya hay uno, se sigue con ése) y editar
// sus valores y motivos. Las acciones devuelven false si no hay borrador, por ejemplo porque otro administrador lo
// publicó mientras tanto.
internal sealed class GestionarBorradorReglasHandler(IReglasRepository versiones, ICurrentTenant tenant)
{
    public async Task NuevaVersionAsync(CancellationToken ct)
    {
        if (await versiones.ObtenerBorradorAsync(ct) is not null)
        {
            return;
        }

        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var base_ = await versiones.UltimaPublicadaAsync(ct)
            ?? throw new DomainException("El operador no tiene reglas publicadas para copiar.");
        var borrador = VersionReglas.CrearBorrador(operadorId, await versiones.SiguienteNumeroAsync(ct), base_.Valores(),
            base_.Motivos.Select(m => m.Datos()));
        versiones.Agregar(borrador);
        await versiones.GuardarCambiosAsync(ct);
    }

    public Task<bool> GuardarValoresAsync(DatosReglas valores, CancellationToken ct)
    {
        return EditarAsync(borrador => borrador.Modificar(valores), ct);
    }

    public Task<bool> AgregarMotivoAsync(DatosMotivo motivo, CancellationToken ct)
    {
        return EditarAsync(borrador => borrador.AgregarMotivo(motivo), ct);
    }

    public Task<bool> QuitarMotivoAsync(Guid motivoId, CancellationToken ct)
    {
        return EditarAsync(borrador => borrador.QuitarMotivo(motivoId), ct);
    }

    private async Task<bool> EditarAsync(Action<VersionReglas> cambio, CancellationToken ct)
    {
        if (await versiones.ObtenerBorradorAsync(ct) is not VersionReglas borrador)
        {
            return false;
        }

        cambio(borrador);
        await versiones.GuardarCambiosAsync(ct);
        return true;
    }
}
