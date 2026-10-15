using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Operadores;

// El archivo del logo del operador, guardado en PostgreSQL (propuesta de stack, sección 3.9). Es del operador: el filtro
// "Tenant" lo aísla igual que al resto de sus datos.
internal sealed class LogoOperador : Entity, IOperadorOwned
{
    public const int MaximoBytes = 1024 * 1024;

    private static readonly string[] TiposPermitidos = ["image/png", "image/jpeg", "image/svg+xml"];

    public Guid OperadorId { get; private set; }
    public string TipoContenido { get; private set; } = "";
    public byte[] Contenido { get; private set; } = [];

    private LogoOperador() { } // para EF Core

    // CU-07, A1: PNG, JPG o SVG, de hasta 1 MB.
    public static LogoOperador Crear(Guid operadorId, string tipoContenido, byte[] contenido)
    {
        if (contenido.Length is < 1 or > MaximoBytes || !TiposPermitidos.Contains(tipoContenido))
        {
            throw new DomainException("El logo tiene que ser PNG, JPG o SVG y no superar 1 MB.");
        }

        return new LogoOperador
        {
            OperadorId = operadorId,
            TipoContenido = tipoContenido,
            Contenido = contenido.ToArray(),
        };
    }
}
