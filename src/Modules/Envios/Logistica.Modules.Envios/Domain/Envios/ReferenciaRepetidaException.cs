using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// El índice único rechazó un envío con una referencia del comercio que ya existe (CU-11, A4): otra importación lo
// creó entre la consulta y el guardado. Quien la recibe informa el envío existente en lugar de crear otro.
internal sealed class ReferenciaRepetidaException()
    : DomainException("Ya existe un envío con esa referencia del comercio.");
