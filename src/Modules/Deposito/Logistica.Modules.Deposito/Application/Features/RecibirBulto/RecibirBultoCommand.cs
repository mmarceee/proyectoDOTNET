using Logistica.Modules.Deposito.Domain.Recepciones;

namespace Logistica.Modules.Deposito.Application.Features.RecibirBulto;

// Un escaneo: el código del bulto y, opcionalmente, lo que el operario midió.
internal sealed record RecibirBultoCommand(string CodigoBulto, Medidas Medidas);

// Lo que el operario ve después de escanear (CU-30, paso 3): el envío y qué bultos ya llegaron.
internal sealed record RecepcionRegistrada(
    string NumeroEnvio,
    ResultadoRecepcion Resultado,
    string? Discrepancia,
    IReadOnlyList<EstadoBulto> Bultos,
    bool EnvioEnDeposito);

internal sealed record EstadoBulto(string Codigo, bool Recibido);
