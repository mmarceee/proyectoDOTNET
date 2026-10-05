namespace Logistica.Modules.Envios.Domain.Envios;

// Lo que llega para crear un bulto. El bulto en sí lo crea el envío, que le asigna el código y el inquilino.
internal sealed record DatosBulto(decimal PesoKg, decimal LargoCm, decimal AnchoCm, decimal AltoCm, decimal MontoTarifa);
