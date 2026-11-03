using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Logistica.Api.ApiPublica;

internal static class DocumentacionApi
{
    public const string Version = "v1";
    public const string SoloPruebas = "ConsolaApiSoloPruebas";

    public static void Configurar(IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AddOpenApi(Version, opciones =>
        {
            opciones.ShouldInclude = descripcion => descripcion.GroupName == Version;
            opciones.AddDocumentTransformer((documento, _, _) =>
            {
                documento.Info.Title = "API pública de Logística";
                documento.Info.Version = Version;
                documento.Info.Description =
                    $"""
                    Cada solicitud requiere la cabecera {AutenticarClaveApi.Cabecera}. La clave selecciona la relación comercial y el ambiente.
                    La cuota por relación es de {LimitesApiPublica.Produccion(configuracion)} solicitudes por minuto en producción y {LimitesApiPublica.Prueba(configuracion)} en prueba.
                    Hay un límite adicional de {LimitesApiPublica.PorIp(configuracion)} solicitudes por minuto por IP. Ante un 429, esperá los segundos indicados por Retry-After.
                    La consola ejecuta las operaciones en /api/v1/pruebas. El servidor rechaza las claves de producción en ese destino con 403.
                    """;
                documento.Components ??= new();
                documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                documento.Components.SecuritySchemes[AutenticarClaveApi.Esquema] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = AutenticarClaveApi.Cabecera,
                    Description = "Generá la clave en el Portal del comercio para el operador seleccionado.",
                };
                documento.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(AutenticarClaveApi.Esquema, documento)] = [],
                    }
                ];
                return Task.CompletedTask;
            });
            opciones.AddOperationTransformer((operacion, contexto, _) =>
            {
                var alta = HttpMethods.IsPost(contexto.Description.HttpMethod ?? "");
                if (alta && operacion.RequestBody?.Content?.TryGetValue("application/json", out var entrada) == true)
                {
                    entrada.Example = JsonNode.Parse("""
                        {
                          "referenciaComercio": "PED-1042",
                          "destinatario": {
                            "nombre": "Ana Pérez",
                            "telefono": "099123456",
                            "email": null,
                            "documento": null
                          },
                          "direccion": {
                            "calle": "Av. Italia",
                            "numero": "1234",
                            "localidad": "Montevideo",
                            "departamento": "Montevideo",
                            "codigoPostal": "11300",
                            "referencia": null
                          },
                          "bultos": [{ "pesoKg": 2, "largoCm": 30, "anchoCm": 20, "altoCm": 10 }],
                          "modalidad": "Estandar"
                        }
                        """);
                }

                foreach (var parametro in operacion.Parameters?.OfType<OpenApiParameter>() ?? [])
                    Describir(parametro);

                if (operacion.Responses is not null)
                {
                    foreach (var (codigo, respuesta) in operacion.Responses)
                    {
                        if (respuesta is not OpenApiResponse concreta) continue;
                        concreta.Description = codigo switch
                        {
                            "200" when alta => "La referencia ya existe en esta relación y ambiente. Se devuelve el mismo envío.",
                            "200" => "Consulta exitosa.",
                            "201" => "Se creó un envío y se devuelve su número, tarifa y enlace de seguimiento.",
                            "400" => "El cuerpo, los filtros o la paginación no son válidos.",
                            "401" => "La clave falta, es inválida, fue revocada o la relación está inactiva.",
                            "404" => "El envío no existe o está fuera de la relación o ambiente de la clave.",
                            "422" => "La dirección está fuera de cobertura o un bulto no tiene tarifa aplicable.",
                            "429" => "Se superó el límite de solicitudes.",
                            _ => concreta.Description,
                        };
                        if (codigo == "429")
                        {
                            concreta.Headers ??= new Dictionary<string, IOpenApiHeader>();
                            concreta.Headers["Retry-After"] = new OpenApiHeader
                            {
                                Description = "Segundos que debe esperar el cliente antes de reintentar.",
                                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                            };
                        }
                    }
                }

                return Task.CompletedTask;
            });
        });
    }

    private static void Describir(OpenApiParameter parametro)
    {
        (string? descripcion, JsonNode? ejemplo) = parametro.Name switch
        {
            "numero" => ("Número público de un envío del ambiente de la clave.", JsonValue.Create("ENV-000001")),
            "estado" => ("Creado, EnOperacion, EntregadoParcialmente o Entregado. Actualmente sólo aparecen los estados respaldados por Envíos.", JsonValue.Create("Creado")),
            "fechaDesde" => ("Fecha inicial de creación, inclusiva, en hora de Montevideo.", JsonValue.Create("2026-11-03")),
            "fechaHasta" => ("Fecha final de creación, inclusiva, en hora de Montevideo.", JsonValue.Create("2026-11-05")),
            "referenciaComercio" => ("Coincidencia exacta, con mayúsculas y minúsculas.", JsonValue.Create("PED-1042")),
            "pagina" => ("Número de página, comenzando en 1.", JsonValue.Create(1)),
            "tamanoPagina" => ("Cantidad de resultados entre 1 y 100. Por defecto 20.", JsonValue.Create(5)),
            _ => (null, null),
        };
        if (descripcion is not null)
        {
            parametro.Description = descripcion;
            parametro.Example = ejemplo;
        }
    }

    public static void Map(WebApplication app)
    {
        app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous().ExcludeFromDescription();
        var docs = app.MapGroup("/api/docs").AllowAnonymous().ExcludeFromDescription();
        docs.MapGet("", (HttpContext http) =>
        {
            http.Response.Headers.ContentSecurityPolicy =
                "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
            http.Response.Headers.CacheControl = "no-store";
            return Recurso("index.html", "text/html; charset=utf-8");
        });
        docs.MapGet("/docs.js", () => Recurso("docs.js", "text/javascript; charset=utf-8"));
        docs.MapGet("/docs.css", () => Recurso("docs.css", "text/css; charset=utf-8"));
    }

    private static IResult Recurso(string archivo, string tipo)
    {
        var contenido = typeof(DocumentacionApi).Assembly.GetManifestResourceStream(
            $"Logistica.Api.ApiPublica.Documentacion.{archivo}")
            ?? throw new InvalidOperationException($"Falta el recurso de documentación {archivo}.");
        return Results.Stream(contenido, tipo);
    }
}
