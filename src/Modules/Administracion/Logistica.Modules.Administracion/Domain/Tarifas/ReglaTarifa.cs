using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Tarifas;

// Un precio del cuadro tarifario: para una zona y una modalidad, el precio base de un bulto según su peso y su volumen
// (CU-05). Los rangos incluyen el "desde" y excluyen el "hasta": 0 a 5 kg y 5 a 10 kg no se pisan, y un bulto de
// 5 kg va al segundo. Es parte de una VersionTarifario: sólo ella la crea, y la regla copia su inquilino.
internal sealed class ReglaTarifa : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid VersionTarifarioId { get; private set; }
    public Guid ZonaId { get; private set; }
    public Modalidad Modalidad { get; private set; }
    public decimal PesoDesdeKg { get; private set; }
    public decimal PesoHastaKg { get; private set; }
    public decimal VolumenDesdeM3 { get; private set; }
    public decimal VolumenHastaM3 { get; private set; }
    public decimal PrecioBase { get; private set; }

    private ReglaTarifa() { } // para EF Core

    public ReglaTarifa(Guid operadorId, Guid versionTarifarioId, DatosReglaTarifa datos)
    {
        if (datos.ZonaId == Guid.Empty || !Enum.IsDefined(datos.Modalidad))
        {
            throw new DomainException("La regla necesita una zona y una modalidad.");
        }

        if (datos.PesoDesdeKg < 0 || datos.PesoDesdeKg >= datos.PesoHastaKg
            || datos.VolumenDesdeM3 < 0 || datos.VolumenDesdeM3 >= datos.VolumenHastaM3)
        {
            throw new DomainException("Cada rango de peso y de volumen empieza en cero o más y termina después de empezar.");
        }

        if (datos.PrecioBase < 0)
        {
            throw new DomainException("El precio base no puede ser negativo.");
        }

        OperadorId = operadorId;
        VersionTarifarioId = versionTarifarioId;
        ZonaId = datos.ZonaId;
        Modalidad = datos.Modalidad;
        PesoDesdeKg = datos.PesoDesdeKg;
        PesoHastaKg = datos.PesoHastaKg;
        VolumenDesdeM3 = datos.VolumenDesdeM3;
        VolumenHastaM3 = datos.VolumenHastaM3;
        PrecioBase = datos.PrecioBase;
    }

    // Los valores de la regla, para copiarla a un borrador nuevo (CU-05, paso 4).
    public DatosReglaTarifa Datos() =>
        new(ZonaId, Modalidad, PesoDesdeKg, PesoHastaKg, VolumenDesdeM3, VolumenHastaM3, PrecioBase);

    // ¿Esta regla le pone precio a ese bulto?
    public bool Aplica(Guid zonaId, Modalidad modalidad, decimal pesoKg, decimal volumenM3) =>
        ZonaId == zonaId && Modalidad == modalidad
        && PesoDesdeKg <= pesoKg && pesoKg < PesoHastaKg
        && VolumenDesdeM3 <= volumenM3 && volumenM3 < VolumenHastaM3;

    // CU-05, A1: dos reglas de la misma zona y modalidad se superponen si se cruzan a la vez en peso y en volumen: un
    // bulto de ese cruce tendría dos precios.
    public bool SeSuperponeCon(ReglaTarifa otra) => ZonaId == otra.ZonaId && Modalidad == otra.Modalidad
        && PesoDesdeKg < otra.PesoHastaKg && otra.PesoDesdeKg < PesoHastaKg
        && VolumenDesdeM3 < otra.VolumenHastaM3 && otra.VolumenDesdeM3 < VolumenHastaM3;
}
