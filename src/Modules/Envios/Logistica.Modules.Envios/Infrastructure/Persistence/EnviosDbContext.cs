using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EnviosDbContext(DbContextOptions<EnviosDbContext> options)
    : ModuleDbContext(options, Schema)
{
    public const string Schema = "envios";

    public DbSet<Envio> Envios => Set<Envio>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // De acá sale el número de cada envío: único entre todos los operadores y, por lo tanto, en cada uno.
        modelBuilder.HasSequence<long>("numero_envio");
    }
}
