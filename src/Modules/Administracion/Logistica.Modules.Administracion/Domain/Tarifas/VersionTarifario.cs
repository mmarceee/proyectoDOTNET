using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Tarifas;

// Una versión del cuadro tarifario del operador (CU-05, RF 3). Se edita sólo como borrador; al publicarla queda Vigente
// desde una fecha y no se modifica más (letra 6.6): los envíos admitidos con ella conservan su precio. Cuál rige en un
// momento lo deciden las fechas de vigencia; una Vigente que todavía no empezó se muestra como "programada".
internal sealed class VersionTarifario : Entity, IOperadorOwned
{
    private readonly List<ReglaTarifa> _reglas = [];
    private readonly List<AjusteTarifa> _ajustes = [];

    public Guid OperadorId { get; private set; }
    public int Numero { get; private set; }
    public EstadoVersion Estado { get; private set; }
    public DateTimeOffset? VigenteDesde { get; private set; }
    public DateTimeOffset? VigenteHasta { get; private set; }
    public IReadOnlyList<ReglaTarifa> Reglas => _reglas;
    public IReadOnlyList<AjusteTarifa> Ajustes => _ajustes;

    private VersionTarifario() { } // para EF Core

    // CU-05, pasos 3 y 4: un borrador nuevo con una copia de las reglas y los ajustes de la versión vigente, si hay.
    public static VersionTarifario CrearBorrador(Guid operadorId, int numero, VersionTarifario? copiaDe)
    {
        if (operadorId == Guid.Empty || numero < 1)
        {
            throw new DomainException("La versión necesita operador y un número válido.");
        }

        var version = new VersionTarifario { OperadorId = operadorId, Numero = numero, Estado = EstadoVersion.Borrador };
        foreach (var regla in copiaDe?.Reglas ?? [])
        {
            version.AgregarRegla(regla.Datos());
        }

        foreach (var ajuste in copiaDe?.Ajustes ?? [])
        {
            version.AgregarAjuste(ajuste.Datos());
        }

        return version;
    }

    public ReglaTarifa AgregarRegla(DatosReglaTarifa datos)
    {
        ExigirBorrador();
        var regla = new ReglaTarifa(OperadorId, Id, datos);
        _reglas.Add(regla);
        return regla;
    }

    public void QuitarRegla(Guid reglaId)
    {
        ExigirBorrador();
        _reglas.RemoveAll(r => r.Id == reglaId);
    }

    public AjusteTarifa AgregarAjuste(DatosAjusteTarifa datos)
    {
        ExigirBorrador();
        var ajuste = new AjusteTarifa(OperadorId, Id, datos);
        _ajustes.Add(ajuste);
        return ajuste;
    }

    public void QuitarAjuste(Guid ajusteId)
    {
        ExigirBorrador();
        _ajustes.RemoveAll(a => a.Id == ajusteId);
    }

    // CU-05, A1: los pares de reglas que se superponen. Un bulto de esos rangos tendría dos precios.
    public IReadOnlyList<(ReglaTarifa Una, ReglaTarifa Otra)> Conflictos()
    {
        var conflictos = new List<(ReglaTarifa, ReglaTarifa)>();
        for (var i = 0; i < _reglas.Count; i++)
        {
            for (var j = i + 1; j < _reglas.Count; j++)
            {
                if (_reglas[i].SeSuperponeCon(_reglas[j]))
                {
                    conflictos.Add((_reglas[i], _reglas[j]));
                }
            }
        }

        return conflictos;
    }

    // CU-05, A2: las combinaciones de zona activa y modalidad sin ninguna regla. Esos envíos se rechazarían en el alta
    // (CU-10, A3), así que se advierte antes de publicar.
    public IReadOnlyList<(Guid ZonaId, Modalidad Modalidad)> CombinacionesSinTarifa(IEnumerable<Guid> zonasActivas) =>
        zonasActivas
            .SelectMany(zona => Enum.GetValues<Modalidad>().Select(modalidad => (zona, modalidad)))
            .Where(c => !_reglas.Any(r => r.ZonaId == c.zona && r.Modalidad == c.modalidad))
            .ToList();

    // CU-05, pasos 7 a 9: el borrador queda vigente desde esa fecha (de inmediato es "desde ahora") y la versión que regía
    // hasta entonces se cierra en ese momento, así nunca hay dos vigentes a la vez.
    public void Publicar(DateTimeOffset desde, DateTimeOffset ahora, VersionTarifario? anterior)
    {
        ExigirBorrador();

        // A3: no se publica hacia atrás; cambiaría el precio de envíos ya admitidos.
        if (desde < ahora)
        {
            throw new DomainException("La fecha de vigencia no puede estar en el pasado.");
        }

        // A1: con reglas superpuestas no se publica.
        if (Conflictos().Count > 0)
        {
            throw new DomainException("Hay reglas superpuestas: un bulto tendría dos precios.");
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

    // ¿Esta versión es la que rige en ese momento?
    public bool RigeEn(DateTimeOffset momento) => Estado != EstadoVersion.Borrador
        && VigenteDesde <= momento && (VigenteHasta is null || momento < VigenteHasta);

    // El precio de un bulto: el de la regla que le corresponde más sus ajustes, nunca menos de cero (CU-05,
    // requerimiento 2; CU-10, paso 7). null si ninguna regla lo cubre (CU-10, A3). El cálculo está en TablaTarifaria,
    // que es lo que se guarda en la caché: así hay una sola forma de calcular.
    public decimal? Calcular(Guid zonaId, Modalidad modalidad, decimal pesoKg, decimal volumenM3) =>
        Tabla().Calcular(zonaId, modalidad, pesoKg, volumenM3);

    // La versión como valores, para la caché y para calcular precios.
    public TablaTarifaria Tabla() =>
        new(Id, Numero, _reglas.Select(r => r.Datos()).ToList(), _ajustes.Select(a => a.Datos()).ToList());

    // La que regía hasta ahora pasa a Reemplazada; conserva sus reglas y su historial.
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
