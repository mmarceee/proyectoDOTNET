using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// Raíz del agregado: protege sus reglas y es el único que crea sus bultos y eventos.
internal sealed class Envio : Entity, IOperadorOwned, IComercioOwned
{
    private readonly List<Bulto> _bultos = [];
    private readonly List<EventoEnvio> _eventos = [];

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Numero { get; private set; } = "";
    public Destinatario Destinatario { get; private set; } = null!;
    public Direccion Direccion { get; private set; } = null!;
    public EstadoEnvio Estado { get; private set; }
    public decimal MontoTarifa { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }

    public IReadOnlyList<Bulto> Bultos => _bultos;
    public IReadOnlyList<EventoEnvio> Eventos => _eventos;

    private Envio() { } // para EF Core

    public static Envio Crear(Guid operadorId, Guid comercioId, string numero, Destinatario destinatario,
        Direccion direccion, IReadOnlyList<DatosBulto> bultos, OrigenEvento origen, Guid? responsableId,
        DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(numero))
        {
            throw new DomainException("El envío necesita un número.");
        }

        if (bultos.Count == 0)
        {
            throw new DomainException("Un envío debe tener al menos un bulto.");
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
    public void Transicionar(EstadoEnvio nuevo, OrigenEvento origen, Guid? responsableId, DateTimeOffset ahora)
    {
        if (!TablaTransiciones.Permite(Estado, nuevo))
        {
            throw new TransicionInvalidaException(Estado, nuevo);
        }

        var anterior = Estado;
        Estado = nuevo;
        _eventos.Add(new EventoEnvio(this, anterior, nuevo, ahora, origen, responsableId));
    }

    // La tarifa del envío es la suma de sus bultos (CU-10).
    private void AgregarBulto(DatosBulto datos)
    {
        var codigo = $"{Numero}-{_bultos.Count + 1}";
        _bultos.Add(new Bulto(this, codigo, datos));
        MontoTarifa = _bultos.Sum(b => b.MontoTarifa);
    }
}
