using Logistica.SharedKernel;

namespace Logistica.Api.Tenancy;

// Provisorio para el hito del 8/10: operador y comercio fijos, leídos de la configuración.
// El 15/10 se reemplaza por el que lee los claims de la cookie de Identity (ADR-0002, sección 2.3).
// Sin configuración devuelve null y el filtro de inquilino no muestra nada (falla cerrado).
internal sealed class TenantProvisorio(IConfiguration configuration) : ICurrentTenant
{
    public Guid? OperadorId { get; } = configuration.GetValue<Guid?>("TenantProvisorio:OperadorId");

    public Guid? ComercioId { get; } = configuration.GetValue<Guid?>("TenantProvisorio:ComercioId");
}
