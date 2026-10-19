using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;

namespace Logistica.Modules.Administracion.Application;

// PROVISORIO hasta el PR de CU-06 (Cristian Reyes): una sola versión de reglas, igual para todos los operadores, para que
// Envíos ya pueda guardar la versión en el alta y pedir las reglas por contrato. Al unir CU-06, se borra este archivo y
// AdministracionModuleApi lee las versiones publicadas. Son las mismas reglas y los mismos Id de motivos que tenía
// ReglasDeEntregaProvisorias en Envíos, así la PWA y las pruebas siguen igual mientras tanto.
internal static class ReglasOperativasProvisorias
{
    public static readonly Guid VersionId = Guid.Parse("a6000000-0000-0000-0000-000000000001");

    public static readonly MotivoNoEntregaResultado Ausente = new(Guid.Parse("e1000000-0000-0000-0000-000000000001"),
        "AUSENTE", "Destinatario ausente", Reprogramable: true, ExigeEvidencia: false);

    public static readonly MotivoNoEntregaResultado SinAcceso = new(Guid.Parse("e1000000-0000-0000-0000-000000000002"),
        "SIN_ACCESO", "Sin acceso al domicilio", Reprogramable: true, ExigeEvidencia: true);

    public static readonly MotivoNoEntregaResultado DireccionInexistente = new(
        Guid.Parse("e1000000-0000-0000-0000-000000000003"), "DIRECCION_INEXISTENTE", "Dirección inexistente",
        Reprogramable: false, ExigeEvidencia: false);

    public static readonly MotivoNoEntregaResultado Rechazado = new(Guid.Parse("e1000000-0000-0000-0000-000000000004"),
        "RECHAZADO", "Rechazado por el destinatario", Reprogramable: false, ExigeEvidencia: false);

    // Sólo la firma; tres intentos con un día entre uno y otro, y devolución automática al agotarlos. MaxParadasPorRuta
    // no lo usa nadie: Planificación lo lee de VersionReglasPlanificacion.
    public static readonly ReglasOperativas Vigentes = new(VersionId, 1,
        MaxIntentos: 3, PlazoEntreIntentos: TimeSpan.FromHours(24),
        ExigeFoto: false, ExigeNombreYDocumento: false,
        DevolverTrasIntentosAgotados: true, PlazoDevolucionDias: 7,
        [new(Modalidad.Estandar, TimeSpan.FromHours(48)), new(Modalidad.Urgente, TimeSpan.FromHours(24))],
        AnticipacionReprogramacion: TimeSpan.FromHours(24),
        MaxParadasPorRuta: 30,
        [Ausente, SinAcceso, DireccionInexistente, Rechazado]);
}
