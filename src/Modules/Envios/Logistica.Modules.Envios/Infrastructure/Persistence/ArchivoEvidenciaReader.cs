using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class ArchivoEvidenciaReader(EnviosDbContext db, ICurrentTenant tenant) : IArchivoEvidenciaReader
{
    public async Task<ArchivoEvidenciaDto?> ConsultarAsync(string numero, Guid archivoId, CancellationToken ct)
    {
        if (tenant.OperadorId is not Guid operadorId)
        {
            return null;
        }

        var comercioId = tenant.ComercioId;
        // El archivo debe pertenecer al envío visible Y estar referenciado por uno de sus intentos.
        return await db.ArchivosEvidencia.AsNoTracking()
            .Where(a => a.Id == archivoId && a.OperadorId == operadorId
                && (!comercioId.HasValue || a.ComercioId == comercioId.Value)
                && db.Envios.Any(e => e.Id == a.EnvioId && e.Numero == numero
                    && e.OperadorId == operadorId && e.ComercioId == a.ComercioId
                    && e.Intentos.Any(i => i.OperadorId == e.OperadorId && i.ComercioId == e.ComercioId
                        && i.Evidencia != null
                        && (i.Evidencia.FirmaArchivoId == a.Id || i.Evidencia.FotoArchivoId == a.Id))))
            .Select(a => new ArchivoEvidenciaDto(a.TipoContenido, a.Contenido))
            .SingleOrDefaultAsync(ct);
    }
}
