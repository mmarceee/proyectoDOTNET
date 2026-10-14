using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// Tablas de ASP.NET Core Identity (usuarios, roles y sus relaciones), en su propio schema.
// Hereda de IdentityDbContext y no de ModuleDbContext: sin filtro "Tenant" ni interceptor, porque Identity busca
// al usuario por correo antes de que exista un inquilino (ADR-0002, sección 2.4). Ningún otro código consulta
// estas tablas: el listado de usuarios de CU-02 filtrará por operador en un único servicio.
internal sealed class IdentidadDbContext(DbContextOptions<IdentidadDbContext> options)
    : IdentityDbContext<Usuario, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "identidad";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Las tablas de Identity: AspNetUsers, AspNetRoles, AspNetUserRoles, etc.
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<Usuario>(usuario =>
        {
            // El Id lo genera Usuario (Guid v7), no la base.
            usuario.Property(u => u.Id).ValueGeneratedNever();
            usuario.Property(u => u.Nombre).HasMaxLength(200).IsRequired();

            // Para listar los usuarios de un operador (CU-02) y los de un comercio.
            usuario.HasIndex(u => u.OperadorId);
            usuario.HasIndex(u => u.ComercioId);
        });
    }
}
