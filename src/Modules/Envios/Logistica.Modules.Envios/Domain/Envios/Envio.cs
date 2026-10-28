using Logistica.SharedKernel;
using Logistica.Modules.Administracion.Contracts;

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
    public Modalidad Modalidad { get; private set; }
    // Nullable para los envíos admitidos con la tarifa provisoria del esqueleto inicial.
    public Guid? VersionTarifarioId { get; private set; }
    public int? VersionTarifarioNumero { get; private set; }
    // La versión de reglas operativas con que se admitió (letra 6.6): CU-17 y CU-18 aplican las de esa versión aunque
    // después se publique otra. Nullable para los envíos admitidos antes de guardarla; esos usan la vigente.
    public Guid? VersionReglasId { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }
    public Guid? ZonaId { get; private set; }
    public Guid? FranjaHorariaId { get; private set; }
    public DateOnly? FechaEntregaProgramada { get; private set; }
    // Después de un intento fallido reprogramado (T9): no se vuelve a intentar antes de esta hora (CU-18).
    public DateTimeOffset? ReintentoDesde { get; private set; }

    // CU-04: los estados en que el envío todavía tiene una entrega por delante, así que su franja es un horario
    // prometido. Entregado, EnDevolucion, Devuelto, Extraviado y Cancelado ya no esperan una entrega.
    public static readonly EstadoEnvio[] EstadosConEntregaPendiente =
    [
        EstadoEnvio.Admitido, EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito,
        EstadoEnvio.NoEntregado, EstadoEnvio.Reprogramado
    ];

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
        DateTimeOffset ahora, DatosVersionTarifario? versionTarifario = null, Guid? versionReglasId = null,
        Modalidad modalidad = Modalidad.Estandar)
    {
        if (string.IsNullOrWhiteSpace(numero))
        {
            throw new DomainException("El envío necesita un número.");
        }

        if (bultos.Count == 0)
        {
            throw new DomainException("Un envío debe tener al menos un bulto.");
        }
        if (!Enum.IsDefined(modalidad))
            throw new DomainException("La modalidad del envío no es válida.");

        if (versionTarifario is not null && (versionTarifario.Id == Guid.Empty || versionTarifario.Numero < 1))
        {
            throw new DomainException("La versión tarifaria debe tener identificador y número válidos.");
        }

        if (versionReglasId == Guid.Empty)
        {
            throw new DomainException("La versión de reglas debe tener un identificador válido.");
        }

        var envio = new Envio
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            Numero = numero,
            Destinatario = destinatario,
            Direccion = direccion,
            Estado = EstadoEnvio.Admitido,
            Modalidad = modalidad,
            // Todos los instantes del envío se guardan en UTC: PostgreSQL no admite otro desfase.
            CreadoEn = ahora.ToUniversalTime(),
            VersionTarifarioId = versionTarifario?.Id,
            VersionTarifarioNumero = versionTarifario?.Numero,
            VersionReglasId = versionReglasId,
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

    // CU-18 (T7 y, en la misma operación, T9 o T10): el repartidor no pudo entregar. Según el motivo, los intentos y la
    // política de devolución, el envío queda Reprogramado, para después del plazo entre intentos, o EnDevolucion. Si
    // queda EnDevolucion, quien llama inicia la Devolucion (CU-20), que es otro agregado.
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
        var agotados = fallidos >= reglas.MaxIntentos;

        // Un motivo no reprogramable devuelve siempre; los intentos agotados, sólo si la política lo dice.
        if (!motivo.Reprogramable || (agotados && reglas.DevolverTrasIntentosAgotados))
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
            var detalle = agotados
                ? $"Se agotaron los {reglas.MaxIntentos} intentos: la política del operador no devuelve sola, decide el despachador."
                : $"Intento {fallidos} de {reglas.MaxIntentos}. Nuevo intento desde {reintento:yyyy-MM-dd HH:mm} UTC.";
            Transicionar(EstadoEnvio.Reprogramado, OrigenEvento.Sistema, null, ocurridoEn, detalle: detalle);
        }

        return intento;
    }

    // CU-19, reprogramación manual: el despachador elige la fecha y la franja del nuevo intento de un envío
    // Reprogramado (T9 la aplicó CU-18). El estado no cambia: sigue Reprogramado, ahora con su nuevo compromiso.
    // comienzoFranja es cuándo empieza esa franja ese día, en la zona horaria del operador: lo calcula quien llama,
    // porque las franjas y las zonas horarias son de Administración.
    public void ElegirNuevoIntento(DateOnly fecha, Guid franjaId, DateTimeOffset comienzoFranja)
    {
        if (Estado != EstadoEnvio.Reprogramado)
        {
            throw new DomainException("Sólo se elige un nuevo intento para un envío reprogramado.");
        }

        // A1: no antes del plazo entre intentos (RF 4).
        if (ReintentoDesde is DateTimeOffset desde && comienzoFranja < desde)
        {
            throw new DomainException(
                $"La franja empieza antes del plazo entre intentos: el envío se puede volver a intentar desde {desde:yyyy-MM-dd HH:mm} UTC.");
        }

        DefinirPlanificacion(ZonaId, franjaId, fecha);
    }

    // CU-21 (T16): el administrador o el despachador declara perdido un envío que estaba en el depósito, en tránsito o
    // en devolución. Extraviado es terminal. La Incidencia de tipo Extravío es otro agregado: la crea quien llama,
    // en la misma transacción.
    public void DeclararExtravio(string motivo, Guid? responsableId, DateTimeOffset ahora)
    {
        // A1: el motivo es obligatorio (guarda de T16).
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new DomainException("Para declarar un extravío hay que indicar el motivo.");
        }

        // A2: si el estado no lo permite (por ejemplo, Entregado), Transicionar lanza TransicionInvalidaException.
        Transicionar(EstadoEnvio.Extraviado, OrigenEvento.Backoffice, responsableId, ahora, detalle: motivo.Trim());
    }

    // CU-20 (T12 o T14; T10 la aplica CU-18): el despachador o el comercio deciden devolver el envío al comercio.
    // La Devolucion es otro agregado: la crea quien llama, en la misma transacción.
    public void IniciarDevolucion(string motivo, OrigenEvento origen, Guid? responsableId, DateTimeOffset ahora)
    {
        // Paso 2: el motivo es obligatorio.
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new DomainException("Para iniciar la devolución hay que indicar el motivo.");
        }

        // A2: el comercio sólo puede pedirla con el envío en el depósito (T12).
        if (origen is OrigenEvento.PortalComercio or OrigenEvento.Api && Estado != EstadoEnvio.EnDeposito)
        {
            throw new DomainException("El comercio sólo puede pedir la devolución de un envío que está en el depósito.");
        }

        // A1: si el estado no lo permite (por ejemplo, EnTransito), Transicionar lanza TransicionInvalidaException.
        Transicionar(EstadoEnvio.EnDevolucion, origen, responsableId, ahora, detalle: motivo.Trim());
    }

    // CU-61: los estados desde los que el destinatario puede elegir otra fecha y franja. NoEntregado no figura porque es
    // transitorio (T11 se eliminó): después de un intento fallido el envío ya está Reprogramado.
    public static readonly EstadoEnvio[] EstadosReprogramablesPorDestinatario =
    [
        EstadoEnvio.Admitido, EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta, EstadoEnvio.Reprogramado
    ];

    // CU-61 (RF 26): el destinatario elige desde el seguimiento público una nueva fecha y franja de entrega.
    // - Admitido: sólo se registra la nueva fecha y franja, sin cambiar de estado.
    // - EnDeposito (T18) o AsignadoARuta (T19): queda Reprogramado. Que la ruta no esté despachada y sacarlo de ella lo
    //   resuelve Planificación antes de llamar (la ruta es su agregado).
    // - Reprogramado: elige su nuevo intento, como el despachador en CU-19.
    // comienzoFranja es cuándo empieza la franja ese día, en la zona horaria del operador; anticipación y máximo de
    // intentos salen de las reglas del envío (CU-06). Primero se valida todo y recién después se cambia algo.
    public void ReprogramarPorDestinatario(DateOnly fecha, Guid franjaId, DateTimeOffset comienzoFranja,
        TimeSpan anticipacion, int maxIntentos, DateTimeOffset ahora)
    {
        // A1: ya salió a reparto o terminó.
        if (!EstadosReprogramablesPorDestinatario.Contains(Estado))
        {
            throw new DomainException("El envío ya no se puede reprogramar: salió a reparto o ya terminó.");
        }

        // A2: la nueva franja tiene que empezar con la anticipación mínima del operador.
        if (comienzoFranja < ahora + anticipacion)
        {
            throw new DomainException(
                $"La nueva entrega tiene que pedirse con al menos {anticipacion.TotalHours:0.#} horas de anticipación.");
        }

        if (ZonaId is null)
        {
            throw new DomainException("El envío todavía no tiene zona: no se puede elegir una franja.");
        }

        if (Estado == EstadoEnvio.Reprogramado)
        {
            // A2: con los intentos agotados decide el despachador (CU-18 o CU-20), no el destinatario.
            if (_intentos.Count(i => i.Resultado == ResultadoIntento.Fallido) >= maxIntentos)
            {
                throw new DomainException("Se agotaron los intentos de entrega: el envío ya no se puede reprogramar.");
            }

            // Respeta además el plazo entre intentos (CU-19, A1).
            ElegirNuevoIntento(fecha, franjaId, comienzoFranja);
            return;
        }

        if (Estado != EstadoEnvio.Admitido)
        {
            // T18 o T19. Después de la transición el envío ya acepta un nuevo compromiso.
            Transicionar(EstadoEnvio.Reprogramado, OrigenEvento.SeguimientoPublico, null, ahora,
                detalle: $"El destinatario pidió la entrega para el {fecha:dd/MM/yyyy}.");
        }

        DefinirPlanificacion(ZonaId, franjaId, fecha);
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
