using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Reglas;

// Una versión de las reglas operativas del operador (CU-06, RF 4). Funciona igual que el cuadro tarifario (CU-05): se
// edita sólo como borrador; al publicarla queda Vigente desde una fecha y no se modifica más (letra 6.6), y cuál rige en
// un momento lo deciden las fechas de vigencia.
internal sealed class VersionReglas : Entity, IOperadorOwned
{
    private readonly List<MotivoNoEntrega> _motivos = [];
    private List<PlazoModalidad> _plazos = [];

    public Guid OperadorId { get; private set; }
    public int Numero { get; private set; }
    public EstadoVersion Estado { get; private set; }
    public DateTimeOffset? VigenteDesde { get; private set; }
    public DateTimeOffset? VigenteHasta { get; private set; }

    public int MaxIntentos { get; private set; }
    public int PlazoEntreIntentosHoras { get; private set; }
    public PruebaExigida PruebaExigida { get; private set; } = null!;
    public PoliticaDevolucion PoliticaDevolucion { get; private set; } = null!;
    public IReadOnlyList<PlazoModalidad> Plazos => _plazos;
    public int AnticipacionReprogramacionHoras { get; private set; }
    public int MaxParadasPorRuta { get; private set; }
    public IReadOnlyList<MotivoNoEntrega> Motivos => _motivos;

    private VersionReglas() { } // para EF Core

    // CU-06, paso 3: un borrador nuevo con los valores y los motivos que se le pasan, normalmente una copia de la vigente.
    public static VersionReglas CrearBorrador(Guid operadorId, int numero, DatosReglas valores, IEnumerable<DatosMotivo> motivos)
    {
        if (operadorId == Guid.Empty || numero < 1)
        {
            throw new DomainException("La versión necesita operador y un número válido.");
        }

        var version = new VersionReglas { OperadorId = operadorId, Numero = numero, Estado = EstadoVersion.Borrador };
        version.Modificar(valores);
        foreach (var motivo in motivos)
        {
            version.AgregarMotivo(motivo);
        }

        return version;
    }

    // Los valores actuales, para copiarlos a un borrador nuevo o mostrarlos en el formulario.
    public DatosReglas Valores() => new(MaxIntentos, PlazoEntreIntentosHoras, PruebaExigida, PoliticaDevolucion,
        _plazos.ToList(), AnticipacionReprogramacionHoras, MaxParadasPorRuta);

    // CU-06, paso 4: se guardan tal cual; se validan al publicar (A1).
    public void Modificar(DatosReglas valores)
    {
        ExigirBorrador();
        MaxIntentos = valores.MaxIntentos;
        PlazoEntreIntentosHoras = valores.PlazoEntreIntentosHoras;
        PruebaExigida = valores.PruebaExigida;
        PoliticaDevolucion = valores.PoliticaDevolucion;
        _plazos = valores.Plazos.ToList();
        AnticipacionReprogramacionHoras = valores.AnticipacionReprogramacionHoras;
        MaxParadasPorRuta = valores.MaxParadasPorRuta;
    }

    public MotivoNoEntrega AgregarMotivo(DatosMotivo datos)
    {
        ExigirBorrador();
        var motivo = new MotivoNoEntrega(OperadorId, Id, datos);
        _motivos.Add(motivo);
        return motivo;
    }

    public void QuitarMotivo(Guid motivoId)
    {
        ExigirBorrador();
        _motivos.RemoveAll(m => m.Id == motivoId);
    }

    // CU-06, A1 y A2: todo lo que impide publicar, para mostrarlo junto. Vacío si se puede publicar.
    public IReadOnlyList<string> Errores()
    {
        var errores = new List<string>();
        if (MaxIntentos < 1) errores.Add("La cantidad máxima de intentos tiene que ser al menos 1.");
        if (PlazoEntreIntentosHoras <= 0) errores.Add("El plazo entre intentos tiene que ser mayor que cero.");
        if (PoliticaDevolucion.PlazoDevolucionDias <= 0) errores.Add("El plazo de devolución tiene que ser mayor que cero.");
        if (AnticipacionReprogramacionHoras <= 0) errores.Add("La anticipación para reprogramar tiene que ser mayor que cero.");
        if (MaxParadasPorRuta < 1) errores.Add("El máximo de paradas por ruta tiene que ser al menos 1.");

        foreach (var modalidad in Enum.GetValues<Modalidad>())
        {
            if (_plazos.SingleOrDefault(p => p.Modalidad == modalidad) is not { PlazoHoras: > 0 })
            {
                errores.Add($"Falta un plazo mayor que cero para la modalidad {modalidad}.");
            }
        }

        foreach (var repetido in _motivos.GroupBy(m => m.Codigo).Where(g => g.Count() > 1))
        {
            errores.Add($"El código de motivo {repetido.Key} está repetido.");
        }

        // A2: el repartidor necesita al menos un motivo para registrar un intento fallido (CU-18).
        if (_motivos.Count == 0) errores.Add("El catálogo de motivos de no entrega no puede quedar vacío.");

        return errores;
    }

    // CU-06, pasos 5 y 6: igual que el tarifario. La versión que regía se cierra en el momento en que empieza ésta.
    public void Publicar(DateTimeOffset desde, DateTimeOffset ahora, VersionReglas? anterior)
    {
        ExigirBorrador();

        // A3: no se publica hacia atrás; cambiaría las reglas de envíos ya en curso.
        if (desde < ahora)
        {
            throw new DomainException("La fecha de vigencia no puede estar en el pasado.");
        }

        if (Errores() is { Count: > 0 } errores)
        {
            throw new DomainException(string.Join(" ", errores));
        }

        if (anterior is not null)
        {
            if (anterior.VigenteDesde is DateTimeOffset inicio && desde <= inicio)
            {
                throw new DomainException("La versión anterior empieza a regir después de esa fecha. Elegí una fecha posterior.");
            }

            anterior.CerrarVigencia(desde);
        }

        Estado = EstadoVersion.Vigente;
        VigenteDesde = desde;
    }

    public bool RigeEn(DateTimeOffset momento) => Estado != EstadoVersion.Borrador
        && VigenteDesde <= momento && (VigenteHasta is null || momento < VigenteHasta);

    // El plazo comprometido para una modalidad (CU-10, CU-60).
    public TimeSpan PlazoParaModalidad(Modalidad modalidad) =>
        TimeSpan.FromHours(_plazos.SingleOrDefault(p => p.Modalidad == modalidad)?.PlazoHoras ?? 0);

    // ¿El motivo está en el catálogo de esta versión? Se busca por código (CU-18).
    public bool MotivoValido(string codigo) => _motivos.Any(m => m.Codigo == codigo.Trim().ToUpperInvariant());

    private void CerrarVigencia(DateTimeOffset hasta)
    {
        if (Estado != EstadoVersion.Vigente)
        {
            throw new DomainException("Sólo se reemplaza una versión vigente.");
        }

        Estado = EstadoVersion.Reemplazada;
        VigenteHasta = hasta;
    }

    // Una versión publicada no se modifica (letra 6.6).
    private void ExigirBorrador()
    {
        if (Estado != EstadoVersion.Borrador)
        {
            throw new DomainException("Una versión publicada no se modifica: creá una nueva versión.");
        }
    }
}
