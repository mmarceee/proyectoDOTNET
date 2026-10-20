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
    public IEnumerable<Guid> EnvioIds => _paradas.OrderBy(p => p.Orden).SelectMany(p => p.Envios).Select(e => e.EnvioId);
    private Ruta() { }

    public static Ruta Crear(Guid operadorId, DateOnly fecha, Guid repartidorId, Guid vehiculoId,
        IReadOnlyList<EnvioRuta> envios, DateTimeOffset ahora)
    {
        if (operadorId == Guid.Empty || repartidorId == Guid.Empty || vehiculoId == Guid.Empty)
            throw new DomainException("La ruta necesita operador, repartidor y vehículo.");
        if (envios.Count == 0) throw new DomainException("Una ruta nueva debe tener al menos un envío.");
        var ruta = new Ruta { OperadorId = operadorId, Fecha = fecha, RepartidorId = repartidorId,
            VehiculoId = vehiculoId, CreadaEn = ahora, Estado = EstadoRuta.Planificada };
        ruta.AgregarEnvios(envios);
        return ruta;
    }

    // Recibe la selección completa, para que evaluación y persistencia agrupen las mismas visitas.
    public void AgregarEnvios(IReadOnlyList<EnvioRuta> envios)
    {
        ExigirPlanificada();
        if (envios.Count == 0 || envios.Any(e => e.Id == Guid.Empty)
            || envios.Select(e => e.Id).Distinct().Count() != envios.Count
            || EnvioIds.Any(id => !envios.Any(e => e.Id == id)) || envios.Count <= EnvioIds.Count())
            throw new DomainException("La selección debe contener envíos válidos, nuevos y sin duplicados.");
        Reorganizar(envios);
        Actualizar();
    }

    public void ModificarPlanificacion(DateOnly fecha, Guid repartidorId, Guid vehiculoId, IReadOnlyList<EnvioRuta>? envios = null)
    {
        ExigirPlanificada();
        if (repartidorId == Guid.Empty || vehiculoId == Guid.Empty) throw new DomainException("Los recursos son obligatorios.");
        if (Fecha == fecha && RepartidorId == repartidorId && VehiculoId == vehiculoId) return;
        Fecha = fecha; RepartidorId = repartidorId; VehiculoId = vehiculoId;
        if (envios is not null) Reorganizar(envios);
        Actualizar();
    }

    public void QuitarEnvio(Guid envioId)
    {
        ExigirPlanificada();
        var parada = _paradas.SingleOrDefault(p => p.Envios.Any(e => e.EnvioId == envioId)) ?? throw new DomainException("El envío no pertenece a la ruta.");
        parada.Quitar(envioId);
        if (parada.Envios.Count == 0) _paradas.Remove(parada);
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
    public void AnularDespacho(DateOnly hoy)
    {
        if (Estado != EstadoRuta.Despachada || Fecha >= hoy)
            throw new DomainException("Sólo se puede anular el despacho de una ruta anterior que no se inició.");
        Estado = EstadoRuta.Planificada;
        DespachadaEn = null;
        Actualizar();
    }
    public void Finalizar()
    {
        if (Estado is not (EstadoRuta.EnCurso or EstadoRuta.Despachada)) throw new DomainException("La ruta no está en ejecución.");
        Estado = EstadoRuta.Finalizada; Actualizar();
    }
    public void RegistrarCarga(bool tieneEnviosCargados)
    {
        if (Estado is not (EstadoRuta.Despachada or EstadoRuta.EnCurso))
            throw new DomainException("La ruta no está disponible para cargar.");
        if (tieneEnviosCargados) Estado = EstadoRuta.EnCurso;
        Actualizar();
    }
    private void Reorganizar(IReadOnlyList<EnvioRuta> envios)
    {
        var anteriores = _paradas.OrderBy(p => p.Orden).ToList();
        var miembros = anteriores.SelectMany(p => p.Envios).ToDictionary(e => e.EnvioId);
        var grupos = AgrupadorParadas.Agrupar(envios);
        var utilizadas = new HashSet<Guid>();
        var nuevas = new List<Parada>();
        foreach (var grupo in grupos)
        {
            var parada = anteriores.FirstOrDefault(p => !utilizadas.Contains(p.Id)
                && grupo.Envios.Any(e => miembros.TryGetValue(e.Id, out var m) && m.ParadaId == p.Id)) ?? new Parada(this);
            utilizadas.Add(parada.Id);
            nuevas.Add(parada);
        }
        foreach (var parada in anteriores) parada.Vaciar();
        for (var i = 0; i < grupos.Count; i++)
        {
            var parada = nuevas[i]; parada.Definir(grupos[i], i + 1);
            foreach (var envio in grupos[i].Envios)
                parada.Agregar(miembros.GetValueOrDefault(envio.Id) ?? new EnvioParada(parada, envio.Id));
        }
        _paradas.Clear(); _paradas.AddRange(nuevas);
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
