using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Http.Contracts.Seguimiento;
using Logistica.Modules.Ejecucion.Contracts.Events;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Features.VerTablero;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

// CU-66, paso 3: los hechos que llegan por RabbitMQ al Worker (entregas, intentos fallidos, alertas de carga y
// conflictos) se mandan al tablero del operador del sobre, por el backplane de Valkey (decisión G1). Avisar dos veces
// sólo hace que la página vuelva a pedir la foto, así que no necesita inbox; los conflictos quedan guardados en
// Ejecución y la foto los trae aunque este aviso se pierda.
internal sealed class NotificarNovedadTablero(ITableroEnVivo tablero) : IConsumidorIntegracion
{
    public static readonly string[] Tipos =
    [
        RutaDespachada.Tipo, EnvioEnTransito.Tipo, EnvioEntregado.Tipo, IntentoFallidoRegistrado.Tipo,
        EnvioDesasignadoDeRuta.Tipo, EnvioExtraviado.Tipo, DevolucionIniciada.Tipo,
        CargaConDiscrepancias.Tipo, ConflictoSincronizacionRegistrado.Tipo,
    ];

    public static string Cola(string tipo) => $"seguimiento.tablero.{tipo}";

    public Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct)
    {
        return tablero.NovedadAsync(mensaje.OperadorId, Leer(mensaje), ct);
    }

    // Lo que ve el despachador de cada hecho. Alerta: lo que la ficha llama alertas operativas (paso 3).
    public static NovedadTableroResponse Leer(MensajeIntegracion mensaje)
    {
        using var json = JsonDocument.Parse(mensaje.Contenido);
        var raiz = json.RootElement;

        string Texto(string nombre) =>
            raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() ?? "" : "";

        int Cantidad(string nombre) =>
            raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.Array ? valor.GetArrayLength() : 0;

        NovedadTableroResponse Novedad(string texto, bool alerta = false) =>
            new(mensaje.Tipo, texto, alerta, mensaje.OcurridoEn);

        return mensaje.Tipo switch
        {
            RutaDespachada.Tipo => Novedad("Se despachó una ruta."),
            EnvioEnTransito.Tipo => Novedad($"{Texto("Numero")} salió a reparto."),
            EnvioEntregado.Tipo => Novedad($"{Texto("Numero")} se entregó."),
            IntentoFallidoRegistrado.Tipo => Novedad($"Intento fallido en {Texto("Numero")} (motivo {Texto("Motivo")})."),
            EnvioDesasignadoDeRuta.Tipo => Novedad("Un envío salió de su ruta."),
            EnvioExtraviado.Tipo => Novedad($"{Texto("Numero")} se declaró extraviado."),
            DevolucionIniciada.Tipo => Novedad($"{Texto("Numero")} quedó en devolución."),
            CargaConDiscrepancias.Tipo => Novedad(
                $"Carga con diferencias: {Cantidad("EnviosIncompletos")} envíos incompletos y {Cantidad("CodigosSobrantes")} bultos sobrantes.",
                alerta: true),
            ConflictoSincronizacionRegistrado.Tipo => Novedad(
                $"Conflicto de sincronización ({Texto("Resultado")}): {Texto("Motivo")}", alerta: true),
            _ => throw new InvalidDataException("Tipo de evento desconocido para el tablero."),
        };
    }
}
