namespace Logistica.Modules.Envios.Contracts.Events;

public sealed record EnvioDesasignadoDeRuta(Guid MessageId, Guid OperadorId, Guid ComercioId,
    Guid EnvioId, Guid RutaId, Guid? ResponsableId, DateTimeOffset OcurridoEn);
