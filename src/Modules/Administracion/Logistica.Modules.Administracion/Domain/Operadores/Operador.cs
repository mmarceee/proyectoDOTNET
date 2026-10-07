using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Operadores;

// El inquilino de la plataforma (ADR-0002, sección 1.1). No implementa IOperadorOwned: no pertenece a un operador, lo es.
internal sealed class Operador : Entity
{
    public string Nombre { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public string ZonaHoraria { get; private set; } = "";
    public bool Activo { get; private set; }

    private Operador() { } // para EF Core

    private Operador(Guid id) : base(id) { }

    // El id sólo se pasa en los datos iniciales; en el resto de los casos lo genera el dominio.
    public static Operador Crear(string nombre, string slug, string zonaHoraria, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new DomainException("El operador necesita un nombre.");
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainException("El operador necesita un slug.");
        }

        if (string.IsNullOrWhiteSpace(zonaHoraria))
        {
            throw new DomainException("El operador necesita una zona horaria.");
        }

        return new Operador(id ?? Guid.CreateVersion7())
        {
            Nombre = nombre,
            Slug = slug,
            ZonaHoraria = zonaHoraria,
            Activo = true,
        };
    }
}
