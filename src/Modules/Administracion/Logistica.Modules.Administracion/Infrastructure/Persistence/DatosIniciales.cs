using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.Modules.Administracion.Domain.Tarifas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

// Datos iniciales (ADR-0002, sección 2.8): dos operadores con configuración distinta que trabajan con el mismo comercio.
// Corren al iniciar la API, después de las migraciones; ningún endpoint los alcanza (sección 2.4).
// Son idempotentes: lo que ya existe no se vuelve a cargar.
internal sealed class DatosIniciales(IServiceProvider servicios, TimeProvider reloj)
{
    // Los mismos Ids que lee TenantProvisorio de appsettings.json (guía, sección 4).
    public static readonly Guid OperadorDemoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid ComercioDemoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // Segundo operador (15/10): otra zona, otra flota, otras franjas y otro máximo de paradas por ruta.
    public static readonly Guid OperadorEsteId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // Los repartidores que tienen usuario de demostración (UsuariosIniciales los vincula).
    public static readonly Guid RepartidorDemoId = Guid.Parse("41111111-1111-1111-1111-111111111111");
    public static readonly Guid RepartidorEsteId = Guid.Parse("43333333-3333-3333-3333-333333333333");

    public async Task SembrarAsync(CancellationToken ct)
    {
        await SembrarComercioAsync(ct);
        await SembrarOperadorDemoAsync(ct);
        await SembrarOperadorEsteAsync(ct);
        await SembrarTarifarioAsync(OperadorDemoId, DatosTarifarioDemo(), [], ct);
        await SembrarTarifarioAsync(OperadorEsteId, DatosTarifarioEste(),
            [new DatosAjusteTarifa(TipoAjuste.Recargo, true, 30, Modalidad.Urgente)], ct);
    }

    // El comercio es global y trabaja con los dos operadores (letra, §3.3): se crea una sola vez.
    // No es de un operador, así que el interceptor no lo valida y alcanza con un contexto sin inquilino.
    private async Task SembrarComercioAsync(CancellationToken ct)
    {
        await using var db = ContextoDe(null);

        if (await db.Comercios.AnyAsync(c => c.Id == ComercioDemoId, ct))
        {
            return;
        }

        db.Comercios.Add(Comercio.Crear("Tienda Demo SA", "210000000017", ComercioDemoId));
        await db.SaveChangesAsync(ct);
    }

    // El de TenantProvisorio. Su configuración la usa CU-40: zona de Montevideo, franjas de mañana y tarde, hasta 20 paradas.
    private Task SembrarOperadorDemoAsync(CancellationToken ct)
    {
        var id = OperadorDemoId;
        var zona = Zona.Crear(id, "MVD-DEMO", "Montevideo Demo", ["11000", "11100", "11200", "11300", "11400"],
            Guid.Parse("61111111-1111-1111-1111-111111111111"));
        var todosLosDias = Enum.GetValues<DayOfWeek>();

        return SembrarOperadorAsync(
            Operador.Crear("Operador Demo", "operador-demo", "America/Montevideo", id),
            // Marca azul (CU-07).
            new IdentidadVisual("#1A56DB", "#93C5FD", null, "contacto@operador-demo.uy", "+598 2900 0001"),
            "envios@tiendademo.uy",
            [
                Repartidor.Crear(id, "Repartidor Demo 1", "DEMO-1", RepartidorDemoId),
                Repartidor.Crear(id, "Repartidor Demo 2", "DEMO-2", Guid.Parse("42222222-2222-2222-2222-222222222222")),
            ],
            [
                Vehiculo.Crear(id, "DEMO001", "Furgón", 1000, 8, 400, 200, 200, Guid.Parse("51111111-1111-1111-1111-111111111111")),
                Vehiculo.Crear(id, "DEMO002", "Furgón", 500, 4, 300, 150, 150, Guid.Parse("52222222-2222-2222-2222-222222222222")),
            ],
            zona,
            [
                FranjaHoraria.Crear(id, zona.Id, new(9, 0), new(13, 0), todosLosDias, new(2026, 1, 1),
                    Guid.Parse("71111111-1111-1111-1111-111111111111")),
                FranjaHoraria.Crear(id, zona.Id, new(14, 0), new(18, 0), todosLosDias, new(2026, 1, 1),
                    Guid.Parse("72222222-2222-2222-2222-222222222222")),
            ],
            VersionReglasPlanificacion.Publicar(id, 1, 20, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Guid.Parse("81111111-1111-1111-1111-111111111111")),
            ct);
    }

    // Configuración distinta: Ciudad de la Costa, una franja única de lunes a sábado, un furgón y una moto, hasta 8 paradas.
    private Task SembrarOperadorEsteAsync(CancellationToken ct)
    {
        var id = OperadorEsteId;
        var zona = Zona.Crear(id, "CDC-ESTE", "Ciudad de la Costa", ["15000", "15100", "15200", "15300"],
            Guid.Parse("63333333-3333-3333-3333-333333333333"));
        DayOfWeek[] lunesASabado =
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

        return SembrarOperadorAsync(
            Operador.Crear("Envíos del Este", "envios-del-este", "America/Montevideo", id),
            // Marca verde (CU-07): los dos operadores se distinguen a simple vista.
            new IdentidadVisual("#047857", "#A7F3D0", null, "contacto@envios-del-este.uy", "+598 4372 0002"),
            "logistica@tiendademo.uy",
            [
                Repartidor.Crear(id, "Repartidor Este 1", "ESTE-1", RepartidorEsteId),
                Repartidor.Crear(id, "Repartidor Este 2", "ESTE-2", Guid.Parse("44444444-4444-4444-4444-444444444444")),
            ],
            [
                Vehiculo.Crear(id, "ESTE001", "Furgón", 800, 6, 350, 180, 180, Guid.Parse("53333333-3333-3333-3333-333333333333")),
                Vehiculo.Crear(id, "ESTE002", "Moto", 30, 0.2m, 50, 40, 40, Guid.Parse("54444444-4444-4444-4444-444444444444")),
            ],
            zona,
            [
                FranjaHoraria.Crear(id, zona.Id, new(10, 0), new(16, 0), lunesASabado, new(2026, 1, 1),
                    Guid.Parse("73333333-3333-3333-3333-333333333333")),
            ],
            VersionReglasPlanificacion.Publicar(id, 1, 8, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Guid.Parse("83333333-3333-3333-3333-333333333333")),
            ct);
    }

    private async Task SembrarOperadorAsync(Operador operador, IdentidadVisual identidad, string emailContacto, Repartidor[] repartidores,
        Vehiculo[] vehiculos, Zona zona, FranjaHoraria[] franjas, VersionReglasPlanificacion reglas, CancellationToken ct)
    {
        // Un contexto con este operador como inquilino: el interceptor valida cada fila igual que en un request.
        // Con el contexto del request, el segundo operador daría TenantMismatchException.
        await using var db = ContextoDe(operador.Id);

        // Operador no tiene filtro de inquilino: se busca por Id.
        // La identidad se aplica también a un operador ya cargado que todavía tiene la de por defecto.
        var existente = await db.Operadores.SingleOrDefaultAsync(o => o.Id == operador.Id, ct);
        if (existente is not null && existente.Identidad == IdentidadVisual.PorDefecto)
        {
            existente.ActualizarIdentidad(identidad);
        }

        if (existente is null)
        {
            operador.ActualizarIdentidad(identidad);
            db.Operadores.Add(operador);
            db.RelacionesComerciales.Add(
                RelacionComercial.Crear(operador.Id, ComercioDemoId, emailContacto, reloj.GetUtcNow()));
        }

        // El filtro "Tenant" ya limita cada consulta a este operador: no hace falta comparar el OperadorId.
        if (!await db.Repartidores.AnyAsync(ct))
        {
            db.Repartidores.AddRange(repartidores);
        }

        if (!await db.Vehiculos.AnyAsync(ct))
        {
            db.Vehiculos.AddRange(vehiculos);
        }

        if (!await db.Zonas.AnyAsync(z => z.Id == zona.Id, ct))
        {
            db.Zonas.Add(zona);
        }

        if (!await db.Franjas.AnyAsync(ct))
        {
            db.Franjas.AddRange(franjas);
        }

        if (!await db.ReglasPlanificacion.AnyAsync(ct))
        {
            db.ReglasPlanificacion.Add(reglas);
        }

        await db.SaveChangesAsync(ct);
    }

    // CU-05. Demo: precios por modalidad, dos tramos de peso. Este: los mismos precios para las dos modalidades, con
    // tramos más grandes y un recargo del 30 % a Urgente. Así los dos operadores cobran distinto el mismo bulto.
    private static DatosReglaTarifa[] DatosTarifarioDemo()
    {
        var zona = Guid.Parse("61111111-1111-1111-1111-111111111111");
        return
        [
            new(zona, Modalidad.Estandar, 0, 5, 0, 0.1m, 150),
            new(zona, Modalidad.Estandar, 5, 30, 0, 0.5m, 300),
            new(zona, Modalidad.Urgente, 0, 5, 0, 0.1m, 250),
            new(zona, Modalidad.Urgente, 5, 30, 0, 0.5m, 450),
        ];
    }

    private static DatosReglaTarifa[] DatosTarifarioEste()
    {
        var zona = Guid.Parse("63333333-3333-3333-3333-333333333333");
        return
        [
            new(zona, Modalidad.Estandar, 0, 10, 0, 0.2m, 180),
            new(zona, Modalidad.Estandar, 10, 30, 0, 0.5m, 350),
            new(zona, Modalidad.Urgente, 0, 10, 0, 0.2m, 180),
            new(zona, Modalidad.Urgente, 10, 30, 0, 0.5m, 350),
        ];
    }

    // La versión 1 vigente desde el 1/1/2026, sembrada con el operador como inquilino, como el resto.
    private async Task SembrarTarifarioAsync(Guid operadorId, DatosReglaTarifa[] reglas, DatosAjusteTarifa[] ajustes,
        CancellationToken ct)
    {
        await using var db = ContextoDe(operadorId);
        if (await db.VersionesTarifario.AnyAsync(ct))
        {
            return;
        }

        var version = VersionTarifario.CrearBorrador(operadorId, 1, null);
        foreach (var regla in reglas)
        {
            version.AgregarRegla(regla);
        }

        foreach (var ajuste in ajustes)
        {
            version.AgregarAjuste(ajuste);
        }

        // Se publica "como si fuera" el 1/1/2026: por eso ese mismo día se pasa como "ahora" y A3 no lo rechaza.
        var inicio = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        version.Publicar(inicio, inicio, null);
        db.VersionesTarifario.Add(version);
        await db.SaveChangesAsync(ct);
    }

    // Un AdministracionDbContext con el inquilino fijado a mano; las opciones y la unidad de trabajo salen del contenedor
    // (guía de aislamiento, sección 3).
    private AdministracionDbContext ContextoDe(Guid? operadorId)
    {
        return ActivatorUtilities.CreateInstance<AdministracionDbContext>(servicios, new InquilinoFijo(operadorId, null));
    }
}
