using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed class RetiroEnvioRuta : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid RutaId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string NumeroEnvio { get; private set; } = string.Empty;
    public int OrdenAnterior { get; private set; }
    public int ParadasRestantes { get; private set; }
    public long RevisionRuta { get; private set; }
    public Guid? ResponsableId { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }
    private RetiroEnvioRuta() { }

    public static RetiroEnvioRuta Registrar(Ruta ruta, Guid envioId, string numeroEnvio, int ordenAnterior,
        Guid? responsableId, DateTimeOffset ahora)
        => new() { OperadorId = ruta.OperadorId, RutaId = ruta.Id, EnvioId = envioId, NumeroEnvio = numeroEnvio,
            OrdenAnterior = ordenAnterior, ParadasRestantes = ruta.Paradas.Count, RevisionRuta = ruta.Revision,
            ResponsableId = responsableId, OcurridoEn = ahora };
}
