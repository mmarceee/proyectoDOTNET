using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed class Parada : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid RutaId { get; private set; }
    private readonly List<EnvioParada> _envios = [];
    public IReadOnlyList<EnvioParada> Envios => _envios;
    public string ClaveDireccion { get; private set; } = string.Empty;
    public string Direccion { get; private set; } = string.Empty;
    public TimeOnly? Desde { get; private set; }
    public TimeOnly? Hasta { get; private set; }
    public int Orden { get; private set; }
    public EstadoParada Estado { get; private set; }
    public DateTimeOffset? LlegadaEn { get; private set; }
    private Parada() { }
    internal Parada(Ruta ruta)
    { OperadorId = ruta.OperadorId; RutaId = ruta.Id; Estado = EstadoParada.Pendiente; }
    internal void Definir(GrupoParada grupo, int orden)
    { ClaveDireccion = grupo.Clave; Direccion = grupo.Direccion; Desde = grupo.Desde; Hasta = grupo.Hasta; Orden = orden; }
    internal void Vaciar() => _envios.Clear();
    internal void Agregar(EnvioParada envio) { envio.MoverA(this); _envios.Add(envio); }
    internal void Quitar(Guid envioId) => _envios.RemoveAll(e => e.EnvioId == envioId);
    internal void CambiarOrden(int orden) => Orden = orden;
}
