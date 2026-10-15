using Logistica.Modules.Envios.Application;
using Logistica.Modules.Envios.Domain.Envios;

namespace Logistica.Modules.Envios.Infrastructure;

// Provisorio hasta CU-06 (reglas operativas, Cristian): las mismas reglas y el mismo catálogo de motivos para todos los
// operadores. Los Id son fijos para que la PWA pueda ofrecer el catálogo mientras CU-50 no lo descarga.
// Al implementar CU-06, se reemplaza por un adaptador que las pida a Administración con la versión de reglas del envío.
internal sealed class ReglasDeEntregaProvisorias : IReglasDeEntrega
{
    // Sólo la firma, que no se configura; tres intentos con un día entre uno y otro.
    private static readonly ReglasEntrega Reglas = new(exigeFoto: false, exigeNombreYDocumento: false,
        maxIntentos: 3, TimeSpan.FromHours(24));

    public static readonly MotivoNoEntrega Ausente = new(Guid.Parse("e1000000-0000-0000-0000-000000000001"),
        "AUSENTE", reprogramable: true, exigeEvidencia: false);

    public static readonly MotivoNoEntrega SinAcceso = new(Guid.Parse("e1000000-0000-0000-0000-000000000002"),
        "SIN_ACCESO", reprogramable: true, exigeEvidencia: true);

    public static readonly MotivoNoEntrega DireccionInexistente = new(Guid.Parse("e1000000-0000-0000-0000-000000000003"),
        "DIRECCION_INEXISTENTE", reprogramable: false, exigeEvidencia: false);

    public static readonly MotivoNoEntrega Rechazado = new(Guid.Parse("e1000000-0000-0000-0000-000000000004"),
        "RECHAZADO", reprogramable: false, exigeEvidencia: false);

    private static readonly IReadOnlyList<MotivoNoEntrega> Catalogo = [Ausente, SinAcceso, DireccionInexistente, Rechazado];

    public Task<ReglasEntrega> DelEnvioAsync(Envio envio, CancellationToken ct)
    {
        return Task.FromResult(Reglas);
    }

    public Task<MotivoNoEntrega?> MotivoAsync(Envio envio, Guid motivoId, CancellationToken ct)
    {
        return Task.FromResult(Catalogo.SingleOrDefault(m => m.Id == motivoId));
    }
}
