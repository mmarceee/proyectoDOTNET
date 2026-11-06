using Logistica.Modules.Seguimiento.Application.HechosEnvio;
using Logistica.Modules.Seguimiento.Domain.Indicadores;

namespace Logistica.Modules.Seguimiento.Application.Features.RecalcularIndicadores;

// CU-71: registra cada entrega e intento fallido en la tabla de lectura de los indicadores. El alta sólo crea la copia
// del envío (con la zona y la ventana comprometida), así los indicadores no dependen de que otro consumidor la cree.
// Los envíos de prueba no cuentan. La idempotencia (A1) la da el inbox del consumidor.
internal sealed class RegistrarCumplimientoHandler(EnviosSeguidos envios, IRegistrosCumplimientoRepository registros)
{
    public async Task HandleAsync(HechoEnvio hecho, CancellationToken ct)
    {
        var envio = await envios.ObtenerOCrearAsync(hecho, ct);
        if (!hecho.EsAlta && !envio.EsPrueba)
        {
            if (hecho.IntentoFallido)
            {
                var motivo = hecho.Motivo ?? throw new InvalidDataException("El intento fallido no tiene el motivo.");
                registros.Agregar(RegistroCumplimiento.DeIntentoFallido(envio, motivo, hecho.FechaEvento));
            }
            else
            {
                registros.Agregar(RegistroCumplimiento.DeEntrega(envio, hecho.ResponsableId, hecho.FechaEvento));
            }
        }
        await registros.GuardarAsync(ct);
    }
}
