using System.Security.Claims;
using System.Text.Encodings.Web;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Logistica.Api.ApiPublica;

internal sealed class AutenticarClaveApi(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IClavesApiAutenticacion claves) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Esquema = "ClaveApi";
    public const string Cabecera = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Cabecera, out var valores) || valores.Count != 1
            || valores[0] is not { Length: > 0 and <= 64 } secreto)
            return AuthenticateResult.NoResult();

        var clave = await claves.IdentificarAsync(secreto, Context.RequestAborted);
        if (clave is null) return AuthenticateResult.Fail("Clave de API inválida.");

        var identidad = new ClaimsIdentity(
        [
            new Claim(ClaimsDeSesion.OperadorId, clave.OperadorId.ToString()),
            new Claim(ClaimsDeSesion.ComercioId, clave.ComercioId.ToString()),
            new Claim(AccesoApiPublica.AmbienteClaim, clave.EsPrueba ? "true" : "false"),
        ], Esquema);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidad), Esquema));
    }
}

internal sealed class RegistrarUsoClaveApi(RequestDelegate siguiente)
{
    public async Task InvokeAsync(HttpContext http, IClavesApiAutenticacion claves)
    {
        if (http.User.Identity?.AuthenticationType == AutenticarClaveApi.Esquema
            && !await claves.RegistrarUsoAsync(http.Request.Headers[AutenticarClaveApi.Cabecera].ToString(), http.RequestAborted))
        {
            await http.ChallengeAsync(AutenticarClaveApi.Esquema);
            return;
        }
        await siguiente(http);
    }
}
