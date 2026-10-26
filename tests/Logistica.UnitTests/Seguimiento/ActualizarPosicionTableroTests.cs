using Logistica.Http.Contracts.Seguimiento;
using Logistica.Modules.Ejecucion.Contracts.Events;
using Logistica.Modules.Ejecucion.Contracts.Results;
using Logistica.Modules.Seguimiento.Application.Features.VerTablero;

namespace Logistica.UnitTests.Seguimiento;

// CU-53 → CU-66: cada posición nueva va al tablero de su operador; las de una ruta ya rendida, no.
public class ActualizarPosicionTableroTests
{
    private sealed class TableroFalso : ITableroEnVivo
    {
        public List<(Guid OperadorId, PosicionTableroResponse Posicion)> Avisos { get; } = [];

        public Task PosicionAsync(Guid operadorId, PosicionTableroResponse posicion, CancellationToken ct)
        {
            Avisos.Add((operadorId, posicion));
            return Task.CompletedTask;
        }

        public Task NovedadAsync(Guid operadorId, NovedadTableroResponse novedad, CancellationToken ct) => Task.CompletedTask;
    }

    private static PosicionVehiculoResult Posicion(Guid operadorId) => new(Guid.NewGuid(), operadorId, Guid.NewGuid(),
        Guid.NewGuid(), -34.9m, -56.16m, DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);

    [Fact]
    public async Task La_posicion_va_al_tablero_del_operador_del_evento()
    {
        var tablero = new TableroFalso();
        var operador = Guid.NewGuid();
        var posicion = Posicion(operador);

        await new ActualizarPosicionTablero(tablero).ConsumirAsync(new PosicionReportada(posicion, SoloHistorial: false), default);

        var (operadorId, enviada) = Assert.Single(tablero.Avisos);
        Assert.Equal(operador, operadorId);
        Assert.Equal(posicion.Id, enviada.Id);
        Assert.Equal(posicion.VehiculoId, enviada.VehiculoId);
        Assert.Equal(posicion.CapturadaEn, enviada.CapturadaEn);
    }

    // La ruta ya estaba rendida: la posición sólo queda en el historial y no reactiva el vehículo en el mapa.
    [Fact]
    public async Task Una_posicion_solo_de_historial_no_se_manda()
    {
        var tablero = new TableroFalso();

        await new ActualizarPosicionTablero(tablero).ConsumirAsync(
            new PosicionReportada(Posicion(Guid.NewGuid()), SoloHistorial: true), default);

        Assert.Empty(tablero.Avisos);
    }
}
