namespace Logistica.Modules.Envios.Contracts.Events;

// Hecho interno. No crea un aviso externo v1/v2 ni un estado operativo nuevo.
public sealed record CustodiaBultosConciliada(Guid EnvioId, IReadOnlyList<Guid> BultoIds, Guid OperacionId,
    Guid? RutaId, bool EnDeposito, Guid? ResponsableId, DateTimeOffset OcurridoEn)
{
    public const string Tipo = "CustodiaBultosConciliada.v1";
}
