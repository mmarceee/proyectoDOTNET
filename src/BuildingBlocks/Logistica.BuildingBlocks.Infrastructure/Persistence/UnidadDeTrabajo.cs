using System.Data;
using System.Data.Common;
using Logistica.SharedKernel;
using Npgsql;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

// Una conexión por request, compartida por los DbContext de todos los módulos, y la transacción que
// agrupa los cambios de varios módulos (ADR-0001, addendum 3). Con conexiones separadas, PostgreSQL
// no puede confirmar los dos cambios juntos.
public sealed class UnidadDeTrabajo(string? connectionString) : IUnidadDeTrabajo, IDisposable, IAsyncDisposable
{
    public NpgsqlConnection Conexion { get; } = new(connectionString);

    // La lee ModuleDbContext antes de guardar, para sumarse a la transacción en curso.
    internal DbTransaction? Transaccion { get; private set; }

    public async Task EjecutarAsync(Func<Task> trabajo, CancellationToken ct)
    {
        // Si ya hay una unidad en curso, el trabajo se suma a ella.
        if (Transaccion is not null)
        {
            await trabajo();
            return;
        }

        if (Conexion.State != ConnectionState.Open)
        {
            await Conexion.OpenAsync(ct);
        }

        // Si el trabajo lanza una excepción, la transacción se descarta sin confirmar: se revierte todo.
        await using var transaccion = await Conexion.BeginTransactionAsync(ct);
        Transaccion = transaccion;

        try
        {
            await trabajo();
            await transaccion.CommitAsync(ct);
        }
        finally
        {
            Transaccion = null;
        }
    }

    public void Dispose()
    {
        Conexion.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return Conexion.DisposeAsync();
    }
}
