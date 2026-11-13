using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Operadores;

// El inquilino de la plataforma (ADR-0002, sección 1.1). No implementa IOperadorOwned: no pertenece a un operador, lo es.
internal sealed class Operador : Entity
{
    public string Nombre { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public string ZonaHoraria { get; private set; } = "";
    public bool Activo { get; private set; }

    // Logo, colores y contacto (CU-07). Nace con la identidad por defecto.
    public IdentidadVisual Identidad { get; private set; } = IdentidadVisual.PorDefecto;

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
            Identidad = IdentidadVisual.PorDefecto,
        };
    }

    // Administrador general: un operador suspendido no opera. Su personal no inicia sesión, sus comercios no lo pueden
    // elegir y el cierre diario lo saltea. Los datos se conservan, así que se puede volver a activar.
    public void Suspender()
    {
        if (!Activo)
        {
            throw new DomainException("El operador ya está suspendido.");
        }

        Activo = false;
    }

    public void Activar()
    {
        if (Activo)
        {
            throw new DomainException("El operador ya está activo.");
        }

        Activo = true;
    }

    // CU-07: la identidad se reemplaza entera; las reglas de cada dato las valida IdentidadVisual.
    public void ActualizarIdentidad(IdentidadVisual nueva)
    {
        Identidad = nueva ?? throw new DomainException("La identidad visual es obligatoria.");
    }
}
