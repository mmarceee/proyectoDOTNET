using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Logistica.Api.Salud;

// /health informa Unhealthy (503) si PostgreSQL no responde: sin la base la API no puede atender nada. Valkey y
// RabbitMQ no se chequean a propósito, porque sin ellos la API sigue funcionando degradada (ADR-0006, sección 2.6, y
// ADR-0003, outbox) y no corresponde que una plataforma la considere caída. Si el chequeo lanza una excepción,
// HealthCheckService la convierte en Unhealthy.
internal sealed class PostgresHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public const string Nombre = "postgres";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        await using var conexion = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await conexion.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("SELECT 1", conexion);
        await comando.ExecuteScalarAsync(ct);
        return HealthCheckResult.Healthy();
    }
}
