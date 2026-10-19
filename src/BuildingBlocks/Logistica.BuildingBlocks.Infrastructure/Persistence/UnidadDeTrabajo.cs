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
public sealed class UnidadDeTrabajo(string? connectionString) : IUnidadDeTrabajo, IConsultaConsistente, IDisposable, IAsyncDisposable
{
    public NpgsqlConnection Conexion { get; } = new(connectionString);

    // La transacción en curso. Internal: nadie de afuera la confirma ni la revierte por su cuenta; los DbContext se suman
    // a ella con Sumar o SumarAsync.
    internal DbTransaction? Transaccion { get; private set; }

    // Sólo esta unidad puede desenganchar las transacciones compartidas a las que sumó un contexto.
    // Una transacción abierta con BeginTransaction por el propio contexto pertenece a su llamador
    // (por ejemplo, publicador Outbox y consumidor Inbox), aunque no haya una unidad en curso.
    private readonly Dictionary<DbContext, DbTransaction> _contextosSumados = [];

    // Suma el contexto a la transacción en curso o, si la unidad ya terminó, lo desengancha, para que su próximo guardado
    // vuelva a tener una transacción propia. Lo llaman antes de guardar ModuleDbContext y el DbContext de Identity, que
    // no hereda de ModuleDbContext (ADR-0003: el alta de un usuario y su evento se confirman juntos).
    public async Task SumarAsync(DbContext contexto, CancellationToken ct)
    {
        if (DebeCambiarTransaccion(contexto))
        {
            await contexto.Database.UseTransactionAsync(Transaccion, ct);
        }
        RegistrarContexto(contexto);
    }

    public void Sumar(DbContext contexto)
    {
        if (DebeCambiarTransaccion(contexto))
        {
            contexto.Database.UseTransaction(Transaccion);
        }
        RegistrarContexto(contexto);
    }

    private bool DebeCambiarTransaccion(DbContext contexto)
    {
        var actual = contexto.Database.CurrentTransaction?.GetDbTransaction();
        return Transaccion is not null
            ? actual != Transaccion
            : _contextosSumados.TryGetValue(contexto, out var compartida) && actual == compartida;
    }

    private void RegistrarContexto(DbContext contexto)
    {
        if (Transaccion is not null) _contextosSumados[contexto] = Transaccion;
        else _contextosSumados.Remove(contexto);
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

    public async Task<T> LeerAsync<T>(Func<Task<T>> consulta, CancellationToken ct)
    {
        if (Transaccion is not null)
            throw new InvalidOperationException("La consulta consistente requiere su propio ámbito de lectura.");
        if (Conexion.State != ConnectionState.Open) await Conexion.OpenAsync(ct);
        await using var transaccion = await Conexion.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        Transaccion = transaccion;
        try
        {
            await using var comando = new NpgsqlCommand("SET TRANSACTION READ ONLY", Conexion, transaccion);
            await comando.ExecuteNonQueryAsync(ct);
            var resultado = await consulta();
            await transaccion.CommitAsync(ct);
            return resultado;
        }
        finally
        {
            Transaccion = null;
            foreach (var contexto in _contextosSumados.Where(p => p.Value == transaccion).Select(p => p.Key).ToArray())
            {
                await contexto.Database.UseTransactionAsync(null, CancellationToken.None);
                _contextosSumados.Remove(contexto);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        return Conexion.DisposeAsync();
    }
}
