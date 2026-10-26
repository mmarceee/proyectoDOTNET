using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;

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

    public async Task<IReadOnlyList<Envio>> ObtenerPorBultosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Envios.Include(e => e.Bultos).Where(e => e.Bultos.Any(b => bultoIds.Contains(b.Id)))
            .OrderBy(e => e.Numero).ToListAsync(ct);
    }

    public Task GuardarCambiosAsync(CancellationToken ct)
    {
        return db.SaveChangesAsync(ct);
    }

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

    public Task<Guid?> IdPorNumeroAsync(string numero, CancellationToken ct)
    {
        return db.Envios.Where(e => e.Numero == numero).Select(e => (Guid?)e.Id).SingleOrDefaultAsync(ct);
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

    public void RegistrarReprogramacion(Envio envio, DateTimeOffset ahora)
    {
        db.AgregarAlOutbox(EnvioReprogramado.Tipo,
            new EnvioReprogramado(envio.Id, envio.Numero, envio.FechaEntregaProgramada!.Value, envio.FranjaHorariaId!.Value,
                nameof(OrigenEvento.Backoffice)), ahora);
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
