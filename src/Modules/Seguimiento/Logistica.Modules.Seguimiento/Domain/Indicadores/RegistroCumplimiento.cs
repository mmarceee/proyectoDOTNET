using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Domain.Indicadores;

internal enum TipoRegistroCumplimiento
{
    Entrega,
    IntentoFallido,
}

// CU-71: una fila de la tabla de lectura de los indicadores por cada entrega o intento fallido. Los reportes (CU-72)
// agrupan estas filas por período y por zona, repartidor, comercio o motivo, sin consultar las tablas transaccionales.
// Copia del envío seguido lo que se agrupa, así la fila no cambia aunque la copia se actualice después.
internal sealed class RegistroCumplimiento : Entity, IOperadorOwned, IComercioOwned
{
    public const int LargoMotivo = 50;

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid EnvioId { get; private set; }
    public Guid? ZonaId { get; private set; }
    // Sólo en las entregas: IntentoFallidoRegistrado no trae quién lo registró.
    public Guid? RepartidorId { get; private set; }
    public TipoRegistroCumplimiento Tipo { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }
    // Sólo en las entregas de envíos con ventana comprometida: si se entregó antes de que venciera. Null cuando no se
    // puede saber (envíos admitidos antes de guardar la ventana), y esas entregas no cuentan en el porcentaje.
    public bool? ATiempo { get; private set; }
    // Sólo en los intentos fallidos: el código del motivo de no entrega.
    public string? Motivo { get; private set; }

    private RegistroCumplimiento() { } // para EF Core

    public static RegistroCumplimiento DeEntrega(EnvioSeguido envio, Guid? repartidorId, DateTimeOffset entregadoEn)
    {
        var registro = Nuevo(envio, TipoRegistroCumplimiento.Entrega, entregadoEn);
        registro.RepartidorId = repartidorId;
        registro.ATiempo = envio.EntregaComprometidaHasta is { } limite ? registro.OcurridoEn <= limite : null;
        return registro;
    }

    public static RegistroCumplimiento DeIntentoFallido(EnvioSeguido envio, string motivo, DateTimeOffset ocurridoEn)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new DomainException("El intento fallido necesita el motivo de no entrega.");
        var registro = Nuevo(envio, TipoRegistroCumplimiento.IntentoFallido, ocurridoEn);
        var limpio = motivo.Trim();
        registro.Motivo = limpio.Length > LargoMotivo ? limpio[..LargoMotivo] : limpio;
        return registro;
    }

    private static RegistroCumplimiento Nuevo(EnvioSeguido envio, TipoRegistroCumplimiento tipo, DateTimeOffset ocurridoEn)
    {
        if (envio.EsPrueba)
            throw new DomainException("Los envíos de prueba no cuentan en los indicadores.");
        return new RegistroCumplimiento
        {
            OperadorId = envio.OperadorId,
            ComercioId = envio.ComercioId,
            EnvioId = envio.Id,
            ZonaId = envio.ZonaId,
            Tipo = tipo,
            OcurridoEn = ocurridoEn.ToUniversalTime(),
        };
    }
}
