using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Domain.Notificaciones;

// Copia de la identidad visual del operador (CU-07) para los correos al destinatario (CU-62). La actualiza el evento
// IdentidadVisualActualizada: el Worker no consulta Administración. Un operador sin copia usa la marca neutra.
internal sealed class IdentidadOperador : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public string Nombre { get; private set; } = "";
    public string ColorPrimario { get; private set; } = "";
    public string ColorSecundario { get; private set; } = "";
    public string? EmailContacto { get; private set; }
    public string? TelefonoContacto { get; private set; }
    // Fecha del evento que la dejó así: un evento anterior que llega tarde no la pisa (ADR-0003, sección 2.4).
    public DateTimeOffset ActualizadaEn { get; private set; }

    private IdentidadOperador() { }

    // Una por operador: el Id es el del operador.
    private IdentidadOperador(Guid operadorId) : base(operadorId) { }

    public static IdentidadOperador Crear(Guid operadorId, string nombre, string colorPrimario, string colorSecundario,
        string? emailContacto, string? telefonoContacto, DateTimeOffset actualizadaEn)
    {
        if (operadorId == Guid.Empty) throw new DomainException("La identidad necesita el operador.");
        var identidad = new IdentidadOperador(operadorId) { OperadorId = operadorId };
        identidad.Actualizar(nombre, colorPrimario, colorSecundario, emailContacto, telefonoContacto, actualizadaEn);
        return identidad;
    }

    // false si el cambio es anterior al que ya tiene.
    public bool Actualizar(string nombre, string colorPrimario, string colorSecundario, string? emailContacto,
        string? telefonoContacto, DateTimeOffset actualizadaEn)
    {
        if (actualizadaEn < ActualizadaEn) return false;
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(colorPrimario) || string.IsNullOrWhiteSpace(colorSecundario))
            throw new DomainException("La identidad necesita el nombre y los colores del operador.");
        Nombre = nombre.Trim();
        ColorPrimario = colorPrimario.Trim();
        ColorSecundario = colorSecundario.Trim();
        EmailContacto = string.IsNullOrWhiteSpace(emailContacto) ? null : emailContacto.Trim();
        TelefonoContacto = string.IsNullOrWhiteSpace(telefonoContacto) ? null : telefonoContacto.Trim();
        ActualizadaEn = actualizadaEn;
        return true;
    }
}
