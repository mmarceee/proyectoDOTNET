using Logistica.Http.Contracts.Seguimiento;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;

// CU-60, pasos 2 y 3: lo que ve la destinataria de su envío. El inquilino ya salió del token (InquilinoDelToken), así
// que los contratos de Envíos y Administración sólo ven ese operador.
// Es el componente de más tráfico: la respuesta se guarda en la caché 1 minuto con el Id del envío como clave (ADR-0006,
// sección 2.3) y se borra ante cada cambio de estado (InvalidarSeguimiento). El minuto acota lo que no se invalida.
internal sealed class ConsultarSeguimientoHandler(
    IEnviosModuleApi envios, IAdministracionModuleApi administracion, ICacheDistribuida cache)
{
    public const string Cache = "seguimiento";
    private static readonly TimeSpan Duracion = TimeSpan.FromMinutes(1);

    // null si el envío no existe o es de otro operador: el endpoint responde igual que a un token inválido (A2).
    public Task<SeguimientoPublicoResponse?> HandleAsync(Guid envioId, CancellationToken ct)
    {
        return cache.ObtenerOCrearAsync(Cache, Clave(envioId), c => ArmarAsync(envioId, c), Duracion, ct);
    }

    public static string Clave(Guid envioId) => envioId.ToString("N");

    private async Task<SeguimientoPublicoResponse?> ArmarAsync(Guid envioId, CancellationToken ct)
    {
        if (await envios.SeguimientoDeAsync(envioId, ct) is not { } envio
            || await administracion.MarcaDelOperadorAsync(ct) is not { } marca)
        {
            return null;
        }

        var franja = envio is { FechaEntregaProgramada: DateOnly fecha, FranjaHorariaId: Guid franjaId }
            ? (await administracion.ResolverFranjasAsync([franjaId], fecha, ct)).FirstOrDefault()
            : null;

        return new SeguimientoPublicoResponse(
            envio.Numero,
            TextosSeguimiento.Estado(envio.Estado),
            envio.NombreDePila,
            envio.Localidad,
            TextosSeguimiento.Ventana(envio.Estado, envio.FechaEntregaProgramada, franja),
            TextosSeguimiento.PuedeReprogramar(envio.Estado),
            envio.Historial.Select(h => new HitoSeguimientoResponse(TextosSeguimiento.Estado(h.Estado), h.OcurridoEn)).ToList(),
            new MarcaSeguimientoResponse(marca.Nombre, marca.ColorPrimario, marca.ColorSecundario, marca.EmailContacto,
                marca.TelefonoContacto));
    }
}
