using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal static class EnviosPorAmbiente
{
    // ADR-0002 §2.4: al seleccionar envíos de prueba, se conserva el filtro Tenant.
    public static IQueryable<Envio> DelAmbiente(this IQueryable<Envio> envios, bool esPrueba) =>
        esPrueba ? envios.IgnoreQueryFilters([EnvioConfiguration.FiltroPrueba]).Where(e => e.EsPrueba) : envios;
}
