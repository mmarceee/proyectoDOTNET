using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Planificacion;

internal sealed class Repartidor : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }

    // El usuario con el que el repartidor inicia sesión en la PWA (modelo de dominio, Repartidor; CU-02, paso 4).
    // Opcional: un repartidor puede existir antes de tener cuenta.
    public Guid? UsuarioId { get; private set; }

    public string Nombre { get; private set; } = "";
    public string Documento { get; private set; } = "";
    public bool Activo { get; private set; }
    private Repartidor() { }
    private Repartidor(Guid id) : base(id) { }
    public static Repartidor Crear(Guid operador, string nombre, string documento, Guid? id = null)
    {
        if (operador == Guid.Empty || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(documento))
            throw new DomainException("El repartidor necesita operador, nombre y documento.");
        return new(id ?? Guid.CreateVersion7()) { OperadorId = operador, Nombre = nombre.Trim(), Documento = documento.Trim(), Activo = true };
    }

    // Con este vínculo, la sesión del usuario sabe qué repartidor es (claim repartidor_id), y la sincronización
    // verifica que la ruta sea suya (ADR-0004, sección 2.3).
    public void VincularUsuario(Guid usuarioId)
    {
        if (usuarioId == Guid.Empty)
            throw new DomainException("El vínculo necesita un usuario.");
        if (UsuarioId is not null && UsuarioId != usuarioId)
            throw new DomainException("El repartidor ya está vinculado a otro usuario.");
        UsuarioId = usuarioId;
    }
}
