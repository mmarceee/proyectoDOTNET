using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Comercios;

// Entidad global: un comercio puede trabajar con varios operadores, así que no lleva OperadorId (ADR-0002, sección 2.6).
// Los operadores llegan a él sólo a través de su RelacionComercial, que sí tiene filtro.
internal sealed class Comercio : Entity
{
    public string RazonSocial { get; private set; } = "";
    public string DocumentoFiscal { get; private set; } = "";

    private Comercio() { } // para EF Core

    private Comercio(Guid id) : base(id) { }

    // El id sólo se pasa en los datos iniciales; en el resto de los casos lo genera el dominio.
    public static Comercio Crear(string razonSocial, string documentoFiscal, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(razonSocial))
        {
            throw new DomainException("El comercio necesita una razón social.");
        }

        if (string.IsNullOrWhiteSpace(documentoFiscal))
        {
            throw new DomainException("El comercio necesita un documento fiscal.");
        }

        return new Comercio(id ?? Guid.CreateVersion7())
        {
            RazonSocial = razonSocial,
            DocumentoFiscal = documentoFiscal,
        };
    }
}
