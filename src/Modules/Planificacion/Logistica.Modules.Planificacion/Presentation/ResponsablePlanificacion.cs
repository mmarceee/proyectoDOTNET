using System.Security.Claims;
using Logistica.Modules.Planificacion.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Logistica.Modules.Planificacion.Presentation;

internal sealed class ResponsablePlanificacion(IHttpContextAccessor http) : IResponsablePlanificacion
{
    public Guid? Id => Guid.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

