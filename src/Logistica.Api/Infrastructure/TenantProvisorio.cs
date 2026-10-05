using Logistica.SharedKernel;

namespace Logistica.Api.Infrastructure;

internal sealed class TenantProvisorio(
    IConfiguration configuration) : ICurrentTenant
{
    public Guid? OperadorId =>
        configuration.GetValue<Guid?>("TenantProvisorio:OperadorId");

    public Guid? ComercioId => null;
}
