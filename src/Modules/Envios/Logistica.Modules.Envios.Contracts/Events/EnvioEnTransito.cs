namespace Logistica.Modules.Envios.Contracts.Events;

public sealed record EnvioEnTransito(Guid EnvioId, string Numero, DateTimeOffset OcurridoEn, Guid? ResponsableId)
{
    public const string Tipo = "EnvioEnTransito.v1";
}
