using System.Data;
using System.Data.Common;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

// Una conexión por request, compartida por los DbContext de todos los módulos, y la transacción que
// agrupa los cambios de varios módulos (ADR-0001, addendum 3). Con conexiones separadas, PostgreSQL
// no puede confirmar los dos cambios juntos.
public sealed class UnidadDeTrabajo(string? connectionString) : IUnidadDeTrabajo, IDisposable, IAsyncDisposable
{
    public NpgsqlConnection Conexion { get; } = new(connectionString);

    // La transacción en curso. Internal: nadie de afuera la confirma ni la revierte por su cuenta; los DbContext se suman
    // a ella con Sumar o SumarAsync.
    internal DbTransaction? Transaccion { get; private set; }

    // Suma el contexto a la transacción en curso o, si la unidad ya terminó, lo desengancha, para que su próximo guardado
    // vuelva a tener una transacción propia. Lo llaman antes de guardar ModuleDbContext y el DbContext de Identity, que
    // no hereda de ModuleDbContext (ADR-0003: el alta de un usuario y su evento se confirman juntos).
    public async Task SumarAsync(DbContext contexto, CancellationToken ct)
    {
        if (!EstaSumado(contexto))
        {
            await contexto.Database.UseTransactionAsync(Transaccion, ct);
        }
    }

    public void Sumar(DbContext contexto)
    {
        if (!EstaSumado(contexto))
        {
            contexto.Database.UseTransaction(Transaccion);
        }
    }

    // EF recuerda a qué transacción está sumado cada contexto: no hace falta guardarlo aparte.
    private bool EstaSumado(DbContext contexto)
    {
        return contexto.Database.CurrentTransaction?.GetDbTransaction() == Transaccion;
    }

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
