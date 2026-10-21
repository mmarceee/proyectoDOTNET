using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Domain.Avisos;

// Copia mínima de un envío en Seguimiento (CU-64). Se crea con EnvioAdmitido, cuyo sobre trae el comercio porque se
// publica desde su sesión. Los eventos posteriores los publican el operador o el repartidor, sin comercio en el sobre:
// esta copia dice a qué comercio avisar sin leer el inquilino del contenido de esos eventos (ADR-0003, sección 2.2).
internal sealed class EnvioSeguido : Entity, IOperadorOwned, IComercioOwned
{
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Numero { get; private set; } = "";

    private EnvioSeguido() { }

    // El Id es el del envío en Envíos: lo referencia por identificador, sin depender de su dominio.
    private EnvioSeguido(Guid envioId) : base(envioId) { }

    public static EnvioSeguido Crear(Guid operadorId, Guid comercioId, Guid envioId, string numero)
    {
        if (operadorId == Guid.Empty || comercioId == Guid.Empty || envioId == Guid.Empty || string.IsNullOrWhiteSpace(numero))
            throw new DomainException("El envío seguido necesita operador, comercio, identificador y número.");
        return new(envioId) { OperadorId = operadorId, ComercioId = comercioId, Numero = numero };
    }
}
