namespace Logistica.Modules.Envios.Domain.Envios;

// CU-10 recibirá esta referencia de Administración al calcular la tarifa.
internal sealed record DatosVersionTarifario(Guid Id, int Numero);
