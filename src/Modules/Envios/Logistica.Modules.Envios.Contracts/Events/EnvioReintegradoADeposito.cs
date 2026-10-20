namespace Logistica.Modules.Envios.Contracts.Events;

// T8 (CU-55): el envío salió en una ruta, no llegó a intentarse y volvió al depósito con la rendición.
public sealed record EnvioReintegradoADeposito(Guid EnvioId, string Numero, DateTimeOffset OcurridoEn, Guid? ResponsableId)
{
    public const string Tipo = "EnvioReintegradoADeposito.v1";
}
