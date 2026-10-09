using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

// El filtro global "Tenant" (ADR-0002) se aplica al archivo, al envío y a los intentos.
// Las comparaciones entre filas que quedan acá verifican que el archivo sea de ese envío.
internal sealed class ArchivoEvidenciaReader(EnviosDbContext db) : IArchivoEvidenciaReader
{
    public async Task<ArchivoEvidenciaDto?> ConsultarAsync(string numero, Guid archivoId, CancellationToken ct)
    {
        // El archivo debe pertenecer al envío visible Y estar referenciado por uno de sus intentos.
        return await db.ArchivosEvidencia.AsNoTracking()
            .Where(a => a.Id == archivoId
                && db.Envios.Any(e => e.Id == a.EnvioId && e.Numero == numero
                    && e.OperadorId == a.OperadorId && e.ComercioId == a.ComercioId
                    && e.Intentos.Any(i => i.OperadorId == e.OperadorId && i.ComercioId == e.ComercioId
                        && i.Evidencia != null
                        && (i.Evidencia.FirmaArchivoId == a.Id || i.Evidencia.FotoArchivoId == a.Id))))
            .Select(a => new ArchivoEvidenciaDto(a.TipoContenido, a.Contenido))
            .SingleOrDefaultAsync(ct);
    }
}
