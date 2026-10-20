using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed class AnulacionDespacho : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid RutaId { get; private set; }
    public DateOnly FechaRuta { get; private set; }
    public DateTimeOffset DespachadaEn { get; private set; }
    public long RevisionAnterior { get; private set; }
    public long RevisionRuta { get; private set; }
    public string Motivo { get; private set; } = "";
    public Guid? ResponsableId { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }
    private AnulacionDespacho() { }

    public static AnulacionDespacho Registrar(Ruta ruta, DateTimeOffset despacho, long revisionAnterior,
        string motivo, Guid? responsable, DateTimeOffset ahora)
        => new() { OperadorId = ruta.OperadorId, RutaId = ruta.Id, FechaRuta = ruta.Fecha,
            DespachadaEn = despacho, RevisionAnterior = revisionAnterior, RevisionRuta = ruta.Revision,
            Motivo = motivo, ResponsableId = responsable, OcurridoEn = ahora };
}
