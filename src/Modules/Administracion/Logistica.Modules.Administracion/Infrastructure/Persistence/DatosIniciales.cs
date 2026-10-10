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
        await SembrarPlanificacionAsync(ct);
    }

    private async Task SembrarPlanificacionAsync(CancellationToken ct)
    {
        var operador = OperadorDemoId;
        if (!await db.Repartidores.AnyAsync(r => r.OperadorId == operador, ct))
            db.Repartidores.AddRange(Domain.Planificacion.Repartidor.Crear(operador, "Repartidor Demo 1", "DEMO-1", Guid.Parse("41111111-1111-1111-1111-111111111111")),
                Domain.Planificacion.Repartidor.Crear(operador, "Repartidor Demo 2", "DEMO-2", Guid.Parse("42222222-2222-2222-2222-222222222222")));
        if (!await db.Vehiculos.AnyAsync(v => v.OperadorId == operador, ct))
            db.Vehiculos.AddRange(Domain.Planificacion.Vehiculo.Crear(operador, "DEMO001", "Furgón", 1000, 8, 400, 200, 200, Guid.Parse("51111111-1111-1111-1111-111111111111")),
                Domain.Planificacion.Vehiculo.Crear(operador, "DEMO002", "Furgón", 500, 4, 300, 150, 150, Guid.Parse("52222222-2222-2222-2222-222222222222")));
        var zonaId = Guid.Parse("61111111-1111-1111-1111-111111111111");
        if (!await db.Zonas.AnyAsync(z => z.Id == zonaId, ct))
            db.Zonas.Add(Domain.Planificacion.Zona.Crear(operador, "MVD-DEMO", "Montevideo Demo", ["11000", "11100", "11200", "11300", "11400"], zonaId));
        if (!await db.Franjas.AnyAsync(f => f.OperadorId == operador, ct))
        {
            var dias = Enum.GetValues<DayOfWeek>();
            db.Franjas.AddRange(Domain.Planificacion.FranjaHoraria.Crear(operador, zonaId, new(9, 0), new(13, 0), dias,
                new(2026, 1, 1), Guid.Parse("71111111-1111-1111-1111-111111111111")),
                Domain.Planificacion.FranjaHoraria.Crear(operador, zonaId, new(14, 0), new(18, 0), dias, new(2026, 1, 1), Guid.Parse("72222222-2222-2222-2222-222222222222")));
        }
        if (!await db.ReglasPlanificacion.AnyAsync(r => r.OperadorId == operador, ct))
            db.ReglasPlanificacion.Add(Domain.Planificacion.VersionReglasPlanificacion.Publicar(operador, 1, 20,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), Guid.Parse("81111111-1111-1111-1111-111111111111")));
        await db.SaveChangesAsync(ct);
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
