using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// Tablas de ASP.NET Core Identity (usuarios, roles y sus relaciones), en su propio schema.
// Hereda de IdentityDbContext y no de ModuleDbContext: sin filtro "Tenant" ni interceptor, porque Identity busca
// al usuario por correo antes de que exista un inquilino (ADR-0002, sección 2.4). Ningún otro código consulta
// estas tablas: el listado de usuarios de CU-02 filtra por operador en un único servicio (ConsultaUsuarios).
// Usa la conexión de la unidad de trabajo y se suma a su transacción, como los módulos: así el alta de un usuario y
// su evento en el outbox de Administración se confirman juntos (ADR-0003).
internal sealed class IdentidadDbContext(DbContextOptions<IdentidadDbContext> options, UnidadDeTrabajo unidadDeTrabajo)
    : IdentityDbContext<Usuario, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "identidad";

    // Igual que ModuleDbContext: dentro de una unidad de trabajo usa la transacción compartida; fuera de ella,
    // la suya propia. La lógica está una sola vez, en UnidadDeTrabajo.Sumar.
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await unidadDeTrabajo.SumarAsync(this, cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        unidadDeTrabajo.Sumar(this);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

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
