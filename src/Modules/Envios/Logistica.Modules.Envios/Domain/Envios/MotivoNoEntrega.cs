using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// El motivo de un intento fallido, tal como figura en el catálogo de la versión de reglas del envío (modelo de
// dominio, MotivoNoEntrega). El catálogo vive en Administración; Envíos recibe esta copia para decidir si
// reprograma o devuelve (CU-18) y si el motivo exige evidencia.
internal sealed record MotivoNoEntrega
{
    public Guid Id { get; }
    public string Codigo { get; }
    public bool Reprogramable { get; }
    public bool ExigeEvidencia { get; }

    public MotivoNoEntrega(Guid id, string codigo, bool reprogramable, bool exigeEvidencia)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(codigo))
        {
            throw new DomainException("El motivo de no entrega necesita identificador y código.");
        }

        Id = id;
        Codigo = codigo.Trim();
        Reprogramable = reprogramable;
        ExigeEvidencia = exigeEvidencia;
    }
}
