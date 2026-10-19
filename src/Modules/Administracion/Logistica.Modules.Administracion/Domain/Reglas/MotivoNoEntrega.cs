using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Reglas;

// Los valores de un motivo: con ellos se crea y se copia a un borrador nuevo.
internal sealed record DatosMotivo(string Codigo, string Descripcion, bool Reprogramable, bool ExigeEvidencia);

// Un motivo del catálogo de no entrega (CU-06; el repartidor elige uno en CU-18). El código lo identifica entre
// versiones (se guarda en mayúsculas): al copiar una versión, el motivo es una fila nueva con otro Id, pero el mismo
// código. Es parte de una VersionReglas: sólo ella lo crea, y el motivo copia su inquilino.
internal sealed class MotivoNoEntrega : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid VersionReglasId { get; private set; }
    public string Codigo { get; private set; } = "";
    public string Descripcion { get; private set; } = "";
    public bool Reprogramable { get; private set; }
    public bool ExigeEvidencia { get; private set; }

    private MotivoNoEntrega() { } // para EF Core

    public MotivoNoEntrega(Guid operadorId, Guid versionReglasId, DatosMotivo datos)
    {
        if (string.IsNullOrWhiteSpace(datos.Codigo) || string.IsNullOrWhiteSpace(datos.Descripcion))
        {
            throw new DomainException("El motivo necesita un código y una descripción.");
        }

        OperadorId = operadorId;
        VersionReglasId = versionReglasId;
        Codigo = datos.Codigo.Trim().ToUpperInvariant();
        Descripcion = datos.Descripcion.Trim();
        Reprogramable = datos.Reprogramable;
        ExigeEvidencia = datos.ExigeEvidencia;
    }

    public DatosMotivo Datos() => new(Codigo, Descripcion, Reprogramable, ExigeEvidencia);
}
