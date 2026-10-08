using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

internal sealed record PruebaEntrega
{
    public Guid? FirmaArchivoId { get; private init; }
    public Guid? FotoArchivoId { get; private init; }
    public string? NombreReceptor { get; private init; }
    public string? DocumentoReceptor { get; private init; }
    public decimal Latitud { get; private init; }
    public decimal Longitud { get; private init; }
    public DateTimeOffset CapturadaEn { get; private init; }

    private PruebaEntrega() { }

    public PruebaEntrega(Guid? firmaArchivoId, Guid? fotoArchivoId, string? nombreReceptor,
        string? documentoReceptor, Ubicacion ubicacion, DateTimeOffset capturadaEn)
    {
        if (firmaArchivoId == Guid.Empty || fotoArchivoId == Guid.Empty)
        {
            throw new DomainException("Los identificadores de evidencia no pueden estar vacíos.");
        }

        FirmaArchivoId = firmaArchivoId;
        FotoArchivoId = fotoArchivoId;
        NombreReceptor = nombreReceptor;
        DocumentoReceptor = documentoReceptor;
        Latitud = ubicacion.Latitud;
        Longitud = ubicacion.Longitud;
        CapturadaEn = capturadaEn;
    }
}
