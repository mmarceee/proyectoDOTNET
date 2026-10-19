using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Tarifas;

// Un recargo o una bonificación del cuadro tarifario, para una modalidad (CU-05). Un porcentaje se calcula sobre el
// precio base de la regla; un monto fijo se suma o se resta tal cual. Es parte de una VersionTarifario: sólo ella lo
// crea, y el ajuste copia su inquilino.
internal sealed class AjusteTarifa : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid VersionTarifarioId { get; private set; }
    public TipoAjuste Tipo { get; private set; }
    public bool EsPorcentaje { get; private set; }
    public decimal Valor { get; private set; }
    public Modalidad Modalidad { get; private set; }

    private AjusteTarifa() { } // para EF Core

    public AjusteTarifa(Guid operadorId, Guid versionTarifarioId, DatosAjusteTarifa datos)
    {
        if (!Enum.IsDefined(datos.Tipo) || !Enum.IsDefined(datos.Modalidad))
        {
            throw new DomainException("El ajuste necesita un tipo y una modalidad.");
        }

        if (datos.Valor <= 0)
        {
            throw new DomainException("El valor del ajuste tiene que ser mayor a cero.");
        }

        OperadorId = operadorId;
        VersionTarifarioId = versionTarifarioId;
        Tipo = datos.Tipo;
        EsPorcentaje = datos.EsPorcentaje;
        Valor = datos.Valor;
        Modalidad = datos.Modalidad;
    }

    // Los valores del ajuste, para copiarlo a un borrador nuevo (CU-05, paso 4).
    public DatosAjusteTarifa Datos() => new(Tipo, EsPorcentaje, Valor, Modalidad);

    // Cuánto suma (recargo) o resta (bonificación) a un bulto de esta modalidad con ese precio base; cero si es de otra.
    public decimal ImporteSobre(decimal precioBase, Modalidad modalidad)
    {
        if (modalidad != Modalidad)
        {
            return 0;
        }

        var importe = EsPorcentaje ? precioBase * Valor / 100 : Valor;
        return Tipo == TipoAjuste.Recargo ? importe : -importe;
    }
}
