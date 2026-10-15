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
    // Después de un intento fallido reprogramado (T9): no se vuelve a intentar antes de esta hora (CU-18).
    public DateTimeOffset? ReintentoDesde { get; private set; }

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
            // Todos los instantes del envío se guardan en UTC: PostgreSQL no admite otro desfase.
            CreadoEn = ahora.ToUniversalTime(),
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

    // CU-17 (T6): el repartidor entregó el envío con su prueba. La hora es la del dispositivo (RF 20).
    // Primero se valida todo y recién después se cambia algo: si una validación falla, el envío queda igual.
    public IntentoEntrega RegistrarEntrega(PruebaEntrega prueba, ReglasEntrega reglas, Guid? responsableId,
        DateTimeOffset ocurridoEn)
    {
        ExigirTransicion(EstadoEnvio.Entregado);

        if (reglas.ExigeFoto && prueba.FotoArchivoId is null)
        {
            throw new DomainException("Las reglas del envío exigen una foto de la entrega.");
        }

        if (reglas.ExigeNombreYDocumento
            && (string.IsNullOrWhiteSpace(prueba.NombreReceptor) || string.IsNullOrWhiteSpace(prueba.DocumentoReceptor)))
        {
            throw new DomainException("Las reglas del envío exigen el nombre y el documento de quien recibe.");
        }

        // La firma la valida IntentoEntrega: un intento exitoso sin firma no se puede crear.
        var intento = AgregarIntento(_intentos.Count + 1, ocurridoEn, ResultadoIntento.Exitoso, null, prueba);
        Transicionar(EstadoEnvio.Entregado, OrigenEvento.AppRepartidor, responsableId, ocurridoEn,
            new Ubicacion(prueba.Latitud, prueba.Longitud));

        return intento;
    }

    // CU-18 (T7 y, en la misma operación, T9 o T10): el repartidor no pudo entregar. Según el motivo y los intentos,
    // el envío queda Reprogramado, para después del plazo entre intentos, o EnDevolucion. Si queda EnDevolucion,
    // quien llama inicia la Devolucion (CU-20), que es otro agregado.
    public IntentoEntrega RegistrarIntentoFallido(MotivoNoEntrega motivo, PruebaEntrega? evidencia, string? observaciones,
        Ubicacion ubicacion, ReglasEntrega reglas, Guid? responsableId, DateTimeOffset ocurridoEn)
    {
        ExigirTransicion(EstadoEnvio.NoEntregado);

        if (motivo.ExigeEvidencia && evidencia?.FotoArchivoId is null)
        {
            throw new DomainException($"El motivo {motivo.Codigo} exige una foto como evidencia.");
        }

        var intento = AgregarIntento(_intentos.Count + 1, ocurridoEn, ResultadoIntento.Fallido, motivo.Id, evidencia,
            string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim());
        Transicionar(EstadoEnvio.NoEntregado, OrigenEvento.AppRepartidor, responsableId, ocurridoEn, ubicacion,
            $"Motivo: {motivo.Codigo}.");

        // NoEntregado es transitorio (T11 se eliminó): la regla del operador decide el estado siguiente.
        var fallidos = _intentos.Count(i => i.Resultado == ResultadoIntento.Fallido);

        if (!motivo.Reprogramable || fallidos >= reglas.MaxIntentos)
        {
            var causa = motivo.Reprogramable
                ? $"Se agotaron los {reglas.MaxIntentos} intentos."
                : $"El motivo {motivo.Codigo} no admite reprogramar.";
            Transicionar(EstadoEnvio.EnDevolucion, OrigenEvento.Sistema, null, ocurridoEn, detalle: causa);
        }
        else
        {
            // En UTC, como todos los instantes que se guardan: la hora del dispositivo trae su desfase local.
            var reintento = (ocurridoEn + reglas.PlazoEntreIntentos).ToUniversalTime();
            ReintentoDesde = reintento;
            Transicionar(EstadoEnvio.Reprogramado, OrigenEvento.Sistema, null, ocurridoEn,
                detalle: $"Intento {fallidos} de {reglas.MaxIntentos}. Nuevo intento desde {reintento:yyyy-MM-dd HH:mm} UTC.");
        }

        return intento;
    }

    // Verifica la transición antes de tocar el envío, para no dejar un intento agregado sin su cambio de estado.
    private void ExigirTransicion(EstadoEnvio nuevo)
    {
        if (!TablaTransiciones.Permite(Estado, nuevo))
        {
            throw new TransicionInvalidaException(Estado, nuevo);
        }
    }

    // La tarifa del envío es la suma de sus bultos (CU-10).
    private void AgregarBulto(DatosBulto datos)
    {
        var codigo = $"{Numero}-{_bultos.Count + 1}";
        _bultos.Add(new Bulto(this, codigo, datos));
        MontoTarifa = _bultos.Sum(b => b.MontoTarifa);
    }
}
