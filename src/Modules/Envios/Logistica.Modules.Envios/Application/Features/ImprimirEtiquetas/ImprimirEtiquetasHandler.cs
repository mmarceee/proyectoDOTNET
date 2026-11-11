using System.Globalization;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application.Features.ImprimirEtiquetas;

internal sealed class ImprimirEtiquetasHandler(IEtiquetasReader reader, IAdministracionModuleApi administracion,
    IGeneradorEtiquetas generador, ICurrentTenant tenant)
{
    public const int MaximoEtiquetas = 200;
    public const int MaximoEnvios = 100;

    public async Task<ResultadoEtiquetas> HandleAsync(IReadOnlyList<string>? numeros, CancellationToken ct)
    {
        // Defensa adicional al endpoint: ninguna consulta se ejecuta sin operador.
        if (tenant.OperadorId is null) return new(null, NoEncontrado: true);
        if (numeros is null || numeros.Count is 0 or > MaximoEnvios || numeros.Any(string.IsNullOrWhiteSpace))
            return new(null, $"Seleccioná entre 1 y {MaximoEnvios} envíos.");

        var unicos = numeros.Select(n => n.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        var envios = await reader.ConsultarAsync(unicos, ct);
        if (envios.Count != unicos.Length) return new(null, NoEncontrado: true);
        if (envios.Any(e => e.Cancelado)) return new(null, "No se pueden imprimir etiquetas de envíos cancelados.");
        if (envios.Any(e => e.TotalBultos == 0) || envios.Sum(e => (long)e.TotalBultos) > MaximoEtiquetas)
            return new(null, $"El lote debe contener entre 1 y {MaximoEtiquetas} etiquetas. Seleccioná menos envíos.");

        var marca = await administracion.MarcaDelOperadorAsync(ct);
        if (marca is null) return new(null, NoEncontrado: true);
        var logo = await administracion.LogoParaEtiquetaAsync(ct);
        var zonas = (await administracion.ConsultarZonasAsync(ct)).ToDictionary(z => z.Id, z => z.Nombre);
        var porNumero = envios.ToDictionary(e => e.Numero, StringComparer.Ordinal);
        var etiquetas = unicos.SelectMany(numero =>
        {
            var envio = porNumero[numero];
            // El sufijo fue asignado al crear el bulto; nunca renumerar por estado ni por orden lexicográfico.
            return envio.Codigos.OrderBy(Posicion).Select(codigo => new EtiquetaBulto(envio.Numero,
                codigo, Posicion(codigo), envio.TotalBultos, envio.Destinatario, envio.Direccion,
                envio.ZonaId is Guid id ? zonas.GetValueOrDefault(id, "Sin zona") : "Sin zona", envio.Modalidad));
        }).ToList();
        ct.ThrowIfCancellationRequested();
        return new(generador.Generar(new(marca.Nombre, logo?.TipoContenido, logo?.Contenido), etiquetas));
    }

    private static int Posicion(string codigo) => int.Parse(codigo.AsSpan(codigo.LastIndexOf('-') + 1), CultureInfo.InvariantCulture);
}
