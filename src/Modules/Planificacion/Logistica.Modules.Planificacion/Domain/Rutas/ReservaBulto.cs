using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

// La pertenencia física no depende del cierre administrativo de la parada o la ruta.
internal sealed class ReservaBulto : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid BultoId { get; private set; }
    public Guid EnvioId { get; private set; }
    public Guid RutaId { get; private set; }
    public Guid ParadaId { get; private set; }
    public DateTimeOffset AbiertaEn { get; private set; }
    public DateTimeOffset? CerradaEn { get; private set; }
    public string? CausaCierre { get; private set; }
    public Guid OperacionId { get; private set; }
    public Guid? ResponsableId { get; private set; }
    public long Revision { get; private set; }
    private ReservaBulto() { }

    public static ReservaBulto Abrir(Ruta ruta, Guid envioId, Guid bultoId, Guid operacion, Guid? actor, DateTimeOffset ahora)
    {
        if (bultoId == Guid.Empty || operacion == Guid.Empty)
            throw new DomainException("La reserva requiere bulto y operación.");
        var parada = ruta.Paradas.Single(p => p.Envios.Any(e => e.EnvioId == envioId));
        return new() { OperadorId = ruta.OperadorId, EnvioId = envioId, BultoId = bultoId,
            RutaId = ruta.Id, ParadaId = parada.Id, AbiertaEn = ahora.ToUniversalTime(), OperacionId = operacion,
            ResponsableId = actor, Revision = 1 };
    }

    public void Reagrupar(Guid paradaId)
    {
        if (CerradaEn is not null || ParadaId == paradaId) return;
        ParadaId = paradaId;
        Revision++;
    }

    public void Cerrar(string causa, DateTimeOffset ahora)
    {
        if (CerradaEn is not null) throw new DomainException("La reserva ya está cerrada.");
        if (string.IsNullOrWhiteSpace(causa)) throw new DomainException("Indicá la causa de liberación.");
        CerradaEn = ahora.ToUniversalTime();
        CausaCierre = causa;
        Revision++;
    }
}
