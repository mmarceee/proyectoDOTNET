using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.Modules.Administracion.Domain.Operadores;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

// Datos iniciales (ADR-0002, sección 2.8). Corren al iniciar la API, después de las migraciones; ningún endpoint los alcanza
// (sección 2.4). Son idempotentes: si un operador ya existe, no se vuelve a cargar.
internal sealed class DatosIniciales(AdministracionDbContext db, TimeProvider reloj)
{
    // Los mismos Ids que lee TenantProvisorio de appsettings.json (guía, sección 4).
    public static readonly Guid OperadorDemoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid ComercioDemoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public async Task SembrarAsync(CancellationToken ct)
    {
        var comercio = await ObtenerOCrearComercioAsync(ComercioDemoId, "Tienda Demo SA", "210000000017", ct);

        // 15/10: segundo operador con configuración distinta. Cada operador se cargará con su inquilino fijado,
        // para que el interceptor valide igual que en producción (ADR-0002, sección 2.8).
        await SembrarOperadorAsync(
            Operador.Crear("Operador Demo", "operador-demo", "America/Montevideo", OperadorDemoId),
            comercio,
            "envios@tiendademo.uy",
            ct);
    }

    private async Task SembrarOperadorAsync(Operador operador, Comercio comercio, string emailContacto, CancellationToken ct)
    {
        // Operador no tiene filtro de inquilino: se puede buscar sin fijar uno.
        if (await db.Operadores.AnyAsync(o => o.Id == operador.Id, ct))
        {
            return;
        }

        db.Operadores.Add(operador);
        db.RelacionesComerciales.Add(
            RelacionComercial.Crear(operador.Id, comercio.Id, emailContacto, reloj.GetUtcNow()));

        await db.SaveChangesAsync(ct);
    }

    // El comercio es global y puede trabajar con varios operadores: se crea una sola vez.
    private async Task<Comercio> ObtenerOCrearComercioAsync(
        Guid id, string razonSocial, string documentoFiscal, CancellationToken ct)
    {
        var existente = await db.Comercios.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (existente is not null)
        {
            return existente;
        }

        var comercio = Comercio.Crear(razonSocial, documentoFiscal, id);
        db.Comercios.Add(comercio);
        await db.SaveChangesAsync(ct);

        return comercio;
    }
}
