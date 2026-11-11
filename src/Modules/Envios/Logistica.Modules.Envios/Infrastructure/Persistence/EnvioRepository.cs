using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

// Las consultas pasan por el filtro global "Tenant" (ADR-0002): sólo ven los envíos de la sesión.
internal sealed class EnvioRepository(EnviosDbContext db) : IEnvioRepository
{
    // SQL fijo, sin datos del usuario. "Value" es el nombre de columna que EF espera en SqlQueryRaw.
    private const string SiguienteNumeroSql = "SELECT nextval('envios.numero_envio') AS \"Value\"";

    public async Task<string> SiguienteNumeroAsync(CancellationToken ct)
    {
        var valor = await db.Database.SqlQueryRaw<long>(SiguienteNumeroSql).SingleAsync(ct);
        return $"ENV-{valor:D6}";
    }

    public Task<Envio?> ObtenerPorCodigoBultoAsync(string codigoBulto, CancellationToken ct)
    {
        return db.Envios
            .Include(e => e.Bultos)
            .SingleOrDefaultAsync(e => e.Bultos.Any(b => b.Codigo == codigoBulto), ct);
    }

    public Task<Envio?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        return db.Envios.SingleOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<Envio?> ObtenerConBultosAsync(Guid id, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Envios.Include(e => e.Bultos).SingleOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<Domain.Devoluciones.Devolucion?> ObtenerDevolucionAsync(Guid envioId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        // RF 31: la abierta, si hay; si no, la última que se entregó al comercio.
        return await db.Devoluciones.Where(d => d.EnvioId == envioId)
            .OrderBy(d => d.Estado == Domain.Devoluciones.EstadoDevolucion.Cerrada).ThenByDescending(d => d.IniciadaEn).ThenByDescending(d => d.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Domain.Devoluciones.Devolucion>> ObtenerDevolucionesAsync(
        IReadOnlyCollection<Guid> envioIds, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Devoluciones.Where(d => envioIds.Contains(d.EnvioId)).ToListAsync(ct);
    }

    public Task<Envio?> ObtenerConIntentosAsync(Guid id, CancellationToken ct)
    {
        return db.Envios.Include(e => e.Intentos).SingleOrDefaultAsync(e => e.Id == id, ct);
    }

    public void Agregar(Envio envio)
    {
        db.Envios.Add(envio);
    }

    public void Agregar(ArchivoEvidencia archivo)
    {
        db.ArchivosEvidencia.Add(archivo);
    }

    public void Agregar(Domain.Devoluciones.Devolucion devolucion)
    {
        db.Devoluciones.Add(devolucion);
    }

    public void Agregar(Domain.Incidencias.Incidencia incidencia)
    {
        db.Incidencias.Add(incidencia);
    }

    public Task<Domain.Incidencias.Incidencia?> ObtenerIncidenciaAsync(Guid id, CancellationToken ct)
    {
        return db.Incidencias.SingleOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<IReadOnlyList<Envio>> ObtenerPorBultosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Envios.Include(e => e.Bultos).Where(e => e.Bultos.Any(b => bultoIds.Contains(b.Id)))
            .OrderBy(e => e.Numero).ToListAsync(ct);
    }

    public async Task GuardarCambiosAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: EnvioConfiguration.IndiceReferencia })
        { throw new ReferenciaRepetidaException(); }
    }

    public async Task<IReadOnlyList<EnvioCerrado>> ListarCerradosAsync(Guid comercioId, DateTimeOffset desde,
        DateTimeOffset hasta, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        // El instante en que llegó al estado final sale de su historial: el evento de esa transición (T6 o T15).
        var cerrados = await db.Envios.AsNoTracking()
            .Where(e => e.ComercioId == comercioId && (e.Estado == EstadoEnvio.Entregado || e.Estado == EstadoEnvio.Devuelto))
            .Select(e => new
            {
                e.Id, e.Numero, e.Estado, e.MontoTarifa,
                LlegoEn = e.Eventos.Where(ev => ev.EstadoNuevo == e.Estado).Max(ev => (DateTimeOffset?)ev.OcurridoEn),
            })
            .Where(e => e.LlegoEn >= desde && e.LlegoEn < hasta)
            .OrderBy(e => e.LlegoEn)
            .ToListAsync(ct);
        return cerrados.Select(e => new EnvioCerrado(e.Id, e.Numero, e.Estado, e.LlegoEn!.Value, e.MontoTarifa)).ToList();
    }

    public async Task<IReadOnlyList<RecargoRegistrado>> ListarRecargosAsync(Guid comercioId, DateTimeOffset desde,
        DateTimeOffset hasta, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        // El filtro "Tenant" deja sólo los del operador de la sesión (ADR-0002); el número sale del envío del recargo.
        return await db.RecargosNuevaVisita.AsNoTracking()
            .Where(r => r.ComercioId == comercioId && r.OcurridoEn >= desde && r.OcurridoEn < hasta)
            .OrderBy(r => r.OcurridoEn)
            .Join(db.Envios, r => r.EnvioId, e => e.Id, (r, e) => new RecargoRegistrado(r.Id, r.EnvioId, e.Numero, r.OcurridoEn,
                r.Monto))
            .ToListAsync(ct);
    }

    public async Task<Envio?> ObtenerPorReferenciaAsync(Guid comercioId, string referencia, bool esPrueba, CancellationToken ct)
    {
        // Tras un guardado rechazado por el índice (CU-11, A4), el contexto se desengancha de la transacción revertida.
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Envios.DelAmbiente(esPrueba).AsNoTracking()
            .SingleOrDefaultAsync(e => e.ComercioId == comercioId && e.ReferenciaComercio == referencia, ct);
    }

    public Task<Envio?> ObtenerPruebaPorNumeroAsync(string numero, CancellationToken ct) =>
        db.Envios.DelAmbiente(esPrueba: true).AsNoTracking().SingleOrDefaultAsync(e => e.Numero == numero, ct);

    public async Task<IReadOnlyList<Envio>> ObtenerParaPlanificacionAsync(IReadOnlyCollection<Guid>? envioIds, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        var consulta = db.Envios.Include(e => e.Bultos).AsQueryable();
        consulta = envioIds is null ? consulta.Where(e => e.Estado == EstadoEnvio.EnDeposito || e.Estado == EstadoEnvio.Reprogramado)
            : consulta.Where(e => envioIds.Contains(e.Id));
        return await consulta.OrderBy(e => e.Numero).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Bulto>> ObtenerBultosPorCodigosAsync(IReadOnlyCollection<string> codigos, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        // Partir de Envíos conserva el filtro del operador también para los códigos sobrantes.
        return await db.Envios.SelectMany(e => e.Bultos).Where(b => codigos.Contains(b.Codigo)).ToListAsync(ct);
    }

    // Se resuelve en la base, sin traer los envíos: sólo importa si existe alguno. Dentro de la transacción de CU-04,
    // que tiene el bloqueo de planificación del operador.
    public async Task<bool> HayComprometidosAsync(IReadOnlyCollection<Guid> franjaIds, DateOnly desde, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Envios.AnyAsync(e => e.FranjaHorariaId != null && franjaIds.Contains(e.FranjaHorariaId.Value)
            && e.FechaEntregaProgramada >= desde && Envio.EstadosConEntregaPendiente.Contains(e.Estado), ct);
    }

    public async Task<IReadOnlyList<Envio>> ListarParaTableroAsync(IReadOnlyCollection<Guid> ids, DateOnly hasta,
    CancellationToken ct)
    {
        return await db.Envios.AsNoTracking()
            .Where(e => ids.Contains(e.Id)
                || (Envio.EstadosConEntregaPendiente.Contains(e.Estado)
                    && e.FechaEntregaProgramada != null && e.FechaEntregaProgramada <= hasta))
            .ToListAsync(ct);
    }

    public Task<Guid?> IdPorNumeroAsync(string numero, CancellationToken ct)
    {
        return db.Envios.Where(e => e.Numero == numero).Select(e => (Guid?)e.Id).SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Envio>> ListarReprogramadosAsync(CancellationToken ct)
    {
        return await db.Envios.AsNoTracking().Include(e => e.Eventos)
            .Where(e => e.Estado == EstadoEnvio.Reprogramado).ToListAsync(ct);
    }

    public Task<Envio?> ObtenerConEventosAsync(Guid id, CancellationToken ct)
    {
        return db.Envios.AsNoTracking().Include(e => e.Eventos).SingleOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<IReadOnlyList<Guid>> SinFechaConFranjasAsync(IReadOnlyCollection<Guid> franjaIds, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Envios.Where(e => e.FranjaHorariaId != null && franjaIds.Contains(e.FranjaHorariaId.Value)
                && e.FechaEntregaProgramada == null && Envio.EstadosConEntregaPendiente.Contains(e.Estado))
            .Select(e => e.Id).ToListAsync(ct);
    }

    // El operador y el comercio del sobre los pone AgregarAlOutbox desde la sesión.
    public void RegistrarAsignacion(Guid envioId, Guid rutaId, Guid? responsableId, DateTimeOffset ahora)
    {
        db.AgregarAlOutbox(EnvioAsignadoARuta.Tipo,
            new EnvioAsignadoARuta(envioId, rutaId, responsableId, nameof(OrigenEvento.Backoffice)), ahora);
    }
    public void RegistrarCancelacion(Envio envio, string? motivo, DateTimeOffset ahora)
    {
        db.AgregarAlOutbox(EnvioCancelado.Tipo,
            new EnvioCancelado(envio.Id, envio.Numero, motivo, nameof(OrigenEvento.PortalComercio)), ahora);
    }

    public void RegistrarReprogramacion(Envio envio, DateTimeOffset ahora, OrigenEvento origen = OrigenEvento.Backoffice)
    {
        db.AgregarAlOutbox(EnvioReprogramado.Tipo,
            new EnvioReprogramado(envio.Id, envio.Numero, envio.FechaEntregaProgramada!.Value, envio.FranjaHorariaId!.Value,
                origen.ToString()), ahora);
    }

    public void RegistrarExtravio(Envio envio, EstadoEnvio estadoAnterior, string motivo, Guid? responsableId,
        DateTimeOffset ahora)
    {
        db.AgregarAlOutbox(EnvioExtraviado.Tipo,
            new EnvioExtraviado(envio.Id, envio.Numero, estadoAnterior.ToString(), motivo.Trim(), responsableId,
                nameof(OrigenEvento.Backoffice)), ahora);
    }

    public void RegistrarDevolucion(Envio envio, Domain.Devoluciones.Devolucion devolucion, EstadoEnvio estadoAnterior,
        Guid? responsableId, OrigenEvento origen, DateTimeOffset ahora)
    {
        db.AgregarAlOutbox(DevolucionIniciada.Tipo,
            new DevolucionIniciada(envio.Id, envio.Numero, devolucion.Id, estadoAnterior.ToString(), devolucion.Motivo,
                responsableId, origen.ToString()), ahora);
    }

    public void RegistrarDesasignacion(Guid envioId, Guid rutaId, Guid? responsableId, DateTimeOffset ahora)
    {
        db.AgregarAlOutbox(EnvioDesasignadoDeRuta.Tipo,
            new EnvioDesasignadoDeRuta(envioId, rutaId, responsableId, nameof(OrigenEvento.Backoffice)), ahora);
    }
}
