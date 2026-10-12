namespace Logistica.Modules.Envios.Contracts.Events;

// Datos del hecho (CU-41). Identificador, inquilino, fecha y traza pertenecen al sobre del Outbox.
public sealed record EnvioDesasignadoDeRuta(Guid EnvioId, Guid RutaId, Guid? ResponsableId, string Origen)
{
    public const string Tipo = "EnvioDesasignadoDeRuta.v1";
}
