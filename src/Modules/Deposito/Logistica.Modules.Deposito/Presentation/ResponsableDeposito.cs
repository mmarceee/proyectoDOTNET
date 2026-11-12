using System.Security.Claims;
using Logistica.Modules.Deposito.Application;
using Microsoft.AspNetCore.Http;

namespace Logistica.Modules.Deposito.Presentation;

internal sealed class ResponsableDeposito(IHttpContextAccessor http) : IResponsableDeposito
{
    public Guid? Id => Guid.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
