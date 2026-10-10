using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed class Ruta : Entity, IOperadorOwned
{
    private readonly List<Parada> _paradas = [];
    public Guid OperadorId { get; private set; }
    public Guid RepartidorId { get; private set; }
    public Guid VehiculoId { get; private set; }
    public DateOnly Fecha { get; private set; }
    public EstadoRuta Estado { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }
    public DateTimeOffset? DespachadaEn { get; private set; }
    public long Revision { get; private set; }
    public bool ReservaActiva { get; private set; }
    public IReadOnlyList<Parada> Paradas => _paradas;
    private Ruta() { }

    public static Ruta Crear(Guid operadorId, DateOnly fecha, Guid repartidorId, Guid vehiculoId,
        IReadOnlyList<Guid> envios, DateTimeOffset ahora)
    {
        if (operadorId == Guid.Empty || repartidorId == Guid.Empty || vehiculoId == Guid.Empty)
            throw new DomainException("La ruta necesita operador, repartidor y vehículo.");
        if (envios.Count == 0) throw new DomainException("Una ruta nueva debe tener al menos un envío.");
        var ruta = new Ruta { OperadorId = operadorId, Fecha = fecha, RepartidorId = repartidorId,
            VehiculoId = vehiculoId, CreadaEn = ahora, Estado = EstadoRuta.Planificada };
        ruta.AgregarEnvios(envios);
        return ruta;
    }

    public void AgregarEnvios(IReadOnlyList<Guid> envios)
    {
        ExigirPlanificada();
        if (envios.Count == 0 || envios.Any(id => id == Guid.Empty)
            || envios.Distinct().Count() != envios.Count || envios.Any(id => _paradas.Any(p => p.EnvioId == id)))
            throw new DomainException("La selección debe contener envíos válidos, nuevos y sin duplicados.");
        var orden = _paradas.Count == 0 ? 0 : _paradas.Max(p => p.Orden);
        foreach (var id in envios) _paradas.Add(new Parada(this, id, ++orden));
        Actualizar();
    }

    public void ModificarPlanificacion(DateOnly fecha, Guid repartidorId, Guid vehiculoId)
    {
        ExigirPlanificada();
        if (repartidorId == Guid.Empty || vehiculoId == Guid.Empty) throw new DomainException("Los recursos son obligatorios.");
        if (Fecha == fecha && RepartidorId == repartidorId && VehiculoId == vehiculoId) return;
        Fecha = fecha; RepartidorId = repartidorId; VehiculoId = vehiculoId;
        Actualizar();
    }

    public void QuitarEnvio(Guid envioId)
    {
        ExigirPlanificada();
        var parada = _paradas.SingleOrDefault(p => p.EnvioId == envioId) ?? throw new DomainException("El envío no pertenece a la ruta.");
        _paradas.Remove(parada);
        var orden = 0;
        foreach (var restante in _paradas.OrderBy(p => p.Orden)) restante.CambiarOrden(++orden);
        Actualizar();
    }

    public void Despachar(DateTimeOffset ahora)
    {
        ExigirPlanificada();
        if (_paradas.Count == 0) throw new DomainException("No se puede despachar una ruta vacía.");
        Estado = EstadoRuta.Despachada; DespachadaEn = ahora; Actualizar();
    }
    public void Iniciar()
    {
        if (Estado != EstadoRuta.Despachada) throw new DomainException("La ruta debe estar despachada.");
        Estado = EstadoRuta.EnCurso; Actualizar();
    }
    public void Finalizar()
    {
        if (Estado is not (EstadoRuta.EnCurso or EstadoRuta.Despachada)) throw new DomainException("La ruta no está en ejecución.");
        Estado = EstadoRuta.Finalizada; Actualizar();
    }
    private void ExigirPlanificada()
    {
        if (Estado != EstadoRuta.Planificada) throw new DomainException("Sólo se puede modificar una ruta Planificada.");
    }
    private void Actualizar()
    {
        Revision++;
        ReservaActiva = Estado is EstadoRuta.Despachada or EstadoRuta.EnCurso || Estado == EstadoRuta.Planificada && _paradas.Count > 0;
    }
}
