using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// Preparación del almacenamiento en PostgreSQL previsto en propuesta-stack, sección 3.9.
internal sealed class ArchivoEvidencia : Entity, IOperadorOwned, IComercioOwned
{
    public const int MaximoBytes = 1024 * 1024;
    public Guid EnvioId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string TipoContenido { get; private set; } = "";
    public byte[] Contenido { get; private set; } = [];

    private ArchivoEvidencia() { }

    public static ArchivoEvidencia Crear(Envio envio, string tipoContenido, byte[] contenido)
    {
        if (contenido.Length is < 1 or > MaximoBytes || tipoContenido is not ("image/png" or "image/jpeg"))
        {
            throw new DomainException("La evidencia debe ser PNG o JPEG y no superar 1 MB.");
        }

        return new ArchivoEvidencia
        {
            EnvioId = envio.Id, OperadorId = envio.OperadorId, ComercioId = envio.ComercioId,
            TipoContenido = tipoContenido, Contenido = contenido.ToArray(),
        };
    }
}
