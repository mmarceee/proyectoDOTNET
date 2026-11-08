using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Api.Seguridad;

// Claves de Data Protection, que cifran la cookie de sesión, los tokens antiforgery del Backoffice y los enlaces de
// invitación de CU-02. Por defecto se guardan en el disco del contenedor, que Container Apps descarta en cada despliegue:
// todos perderían la sesión y las invitaciones vigentes dejarían de valer. Van a PostgreSQL y no a Valkey porque la
// caché puede perderse sin consecuencias (ADR-0006) y estas claves no. No pertenecen a un inquilino: son del sitio.
internal sealed class ClavesProteccionDbContext(DbContextOptions<ClavesProteccionDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = "proteccion";

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
    }
}
