using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

internal sealed class Bulto : Entity, IOperadorOwned, IComercioOwned
{
    public Guid EnvioId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Codigo { get; private set; } = "";
    public decimal PesoKg { get; private set; }
    public decimal LargoCm { get; private set; }
    public decimal AnchoCm { get; private set; }
    public decimal AltoCm { get; private set; }
    public decimal MontoTarifa { get; private set; }
    // Estado operativo del bulto (RF 31). Usa los mismos estados y la misma TablaTransiciones que el envío: las
    // transiciones T1 a T19 se aplican a cada bulto (tabla-de-transiciones-de-estados.md, entrega parcial).
    public EstadoEnvio Estado { get; private set; }
    // Intentos reales fallidos de este bulto (T7). Cargar, reasignar o reintegrar no los consume (RF 33).
    public int IntentosFallidos { get; private set; }
    public DateTimeOffset? ReintentoDesde { get; private set; }
    internal void ProgramarReintento(DateTimeOffset desde) => ReintentoDesde = desde.ToUniversalTime();
    public UbicacionCustodia Custodia { get; private set; }
    public Guid? CustodiaRutaId { get; private set; }
    public Guid? CustodiaResponsableId { get; private set; }
    public DateTimeOffset? CustodiaConfirmadaEn { get; private set; }
    public Guid? CustodiaOperacionId { get; private set; }
    public long RevisionCustodia { get; private set; }
    private readonly List<MovimientoCustodia> _movimientosCustodia = [];
    public IReadOnlyList<MovimientoCustodia> MovimientosCustodia => _movimientosCustodia;

    internal void ConfirmarCustodia(UbicacionCustodia ubicacion, Guid? ruta, Guid? actor,
        Guid operacion, DateTimeOffset ahora, string motivo)
    {
        if (operacion == Guid.Empty || string.IsNullOrWhiteSpace(motivo))
            throw new DomainException("La confirmación de custodia requiere operación y motivo.");
        if (ubicacion == UbicacionCustodia.Repartidor && ruta is null)
            throw new DomainException("La custodia del repartidor requiere ruta.");
        Custodia = ubicacion; CustodiaRutaId = ruta; CustodiaResponsableId = actor;
        CustodiaConfirmadaEn = ahora.ToUniversalTime(); CustodiaOperacionId = operacion; RevisionCustodia++;
        _movimientosCustodia.Add(new(this, ubicacion, ruta, actor, operacion, ahora, motivo));
    }

    private Bulto() { } // para EF Core

    // Sólo lo llama Envio: el bulto copia el inquilino de su envío.
    public Bulto(Envio envio, string codigo, DatosBulto datos)
    {
        if (datos.PesoKg <= 0 || datos.LargoCm <= 0 || datos.AnchoCm <= 0 || datos.AltoCm <= 0)
        {
            throw new DomainException("El peso y las medidas del bulto deben ser mayores a cero.");
        }

        if (datos.MontoTarifa < 0)
        {
            throw new DomainException("La tarifa del bulto no puede ser negativa.");
        }

        EnvioId = envio.Id;
        OperadorId = envio.OperadorId;
        ComercioId = envio.ComercioId;
        Codigo = codigo;
        PesoKg = datos.PesoKg;
        LargoCm = datos.LargoCm;
        AnchoCm = datos.AnchoCm;
        AltoCm = datos.AltoCm;
        MontoTarifa = datos.MontoTarifa;
        Estado = EstadoEnvio.Admitido; // T1
    }

    // Sólo lo llama Envio, que después recalcula su resumen. Un par que no está en la tabla se rechaza.
    internal void Transicionar(EstadoEnvio nuevo)
    {
        if (!TablaTransiciones.Permite(Estado, nuevo))
        {
            throw new TransicionInvalidaException(Estado, nuevo);
        }

        if (nuevo == EstadoEnvio.NoEntregado)
        {
            IntentosFallidos++;
        }

        Estado = nuevo;
    }
}
