using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// Raíz del agregado: protege sus reglas y es el único que crea sus bultos y eventos.
internal sealed class Envio : Entity, IOperadorOwned, IComercioOwned
{
    private readonly List<Bulto> _bultos = [];
    private readonly List<EventoEnvio> _eventos = [];
    private readonly List<IntentoEntrega> _intentos = [];

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Numero { get; private set; } = "";
    public Destinatario Destinatario { get; private set; } = null!;
    public Direccion Direccion { get; private set; } = null!;
    public EstadoEnvio Estado { get; private set; }
    public decimal MontoTarifa { get; private set; }
    // Nullable para los envíos admitidos con la tarifa provisoria del esqueleto inicial.
    public Guid? VersionTarifarioId { get; private set; }
    public int? VersionTarifarioNumero { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }
    public Guid? ZonaId { get; private set; }
    public Guid? FranjaHorariaId { get; private set; }
    public DateOnly? FechaEntregaProgramada { get; private set; }

    public IReadOnlyList<Bulto> Bultos => _bultos;
    public IReadOnlyList<EventoEnvio> Eventos => _eventos;
    public IReadOnlyList<IntentoEntrega> Intentos => _intentos;

    private Envio() { } // para EF Core

    public void DefinirPlanificacion(Guid? zonaId, Guid? franjaId, DateOnly? fecha)
    {
        if (Estado is not (EstadoEnvio.Admitido or EstadoEnvio.EnDeposito or EstadoEnvio.Reprogramado))
            throw new DomainException("No se puede cambiar el compromiso de un envío ya asignado o en ejecución.");
        if (zonaId == Guid.Empty || franjaId == Guid.Empty || (franjaId is not null && zonaId is null))
            throw new DomainException("La franja requiere una zona válida.");
        ZonaId = zonaId; FranjaHorariaId = franjaId; FechaEntregaProgramada = fecha;
    }

    public static Envio Crear(Guid operadorId, Guid comercioId, string numero, Destinatario destinatario,
        Direccion direccion, IReadOnlyList<DatosBulto> bultos, OrigenEvento origen, Guid? responsableId,
        DateTimeOffset ahora, DatosVersionTarifario? versionTarifario = null)
    {
        if (string.IsNullOrWhiteSpace(numero))
        {
            throw new DomainException("El envío necesita un número.");
        }

        if (bultos.Count == 0)
        {
            throw new DomainException("Un envío debe tener al menos un bulto.");
        }

        if (versionTarifario is not null && (versionTarifario.Id == Guid.Empty || versionTarifario.Numero < 1))
        {
            throw new DomainException("La versión tarifaria debe tener identificador y número válidos.");
        }

        var envio = new Envio
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            Numero = numero,
            Destinatario = destinatario,
            Direccion = direccion,
            Estado = EstadoEnvio.Admitido,
            CreadoEn = ahora,
            VersionTarifarioId = versionTarifario?.Id,
            VersionTarifarioNumero = versionTarifario?.Numero,
        };

        foreach (var datos in bultos)
        {
            envio.AgregarBulto(datos);
        }

        // T1: el envío nace Admitido y su historial empieza con este evento.
        envio._eventos.Add(new EventoEnvio(envio, null, EstadoEnvio.Admitido, ahora, origen, responsableId));

        return envio;
    }

    // Único punto que cambia el estado (RF 11) y registra el EventoEnvio (RF 12).
    // Las condiciones propias de cada caso de uso (bultos escaneados, ruta sin despachar...) las
    // controla quien llama; acá sólo se valida que la transición exista en la tabla.
    public void Transicionar(EstadoEnvio nuevo, OrigenEvento origen, Guid? responsableId, DateTimeOffset ahora,
        Ubicacion? ubicacion = null, string? detalle = null)
    {
        if (!TablaTransiciones.Permite(Estado, nuevo))
        {
            throw new TransicionInvalidaException(Estado, nuevo);
        }

        var anterior = Estado;
        Estado = nuevo;
        _eventos.Add(new EventoEnvio(this, anterior, nuevo, ahora, origen, responsableId, ubicacion, detalle));
    }

    // Los CU-17/18 coordinarán este registro con la transición y las reglas de la versión aplicada.
    public IntentoEntrega AgregarIntento(int numero, DateTimeOffset fechaHora, ResultadoIntento resultado,
        Guid? motivoNoEntregaId, PruebaEntrega? evidencia, string? observaciones = null)
    {
        if (_intentos.Any(i => i.NumeroIntento == numero))
        {
            throw new DomainException("El número de intento ya existe en este envío.");
        }

        var intento = new IntentoEntrega(this, numero, fechaHora, resultado, motivoNoEntregaId, evidencia, observaciones);
        _intentos.Add(intento);
        return intento;
    }

    // La tarifa del envío es la suma de sus bultos (CU-10).
    private void AgregarBulto(DatosBulto datos)
    {
        var codigo = $"{Numero}-{_bultos.Count + 1}";
        _bultos.Add(new Bulto(this, codigo, datos));
        MontoTarifa = _bultos.Sum(b => b.MontoTarifa);
    }
}
