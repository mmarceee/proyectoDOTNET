using System.Globalization;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.CrearEnvio;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application.Features.ImportarEnvios;

// CU-11: agrupa las filas en envíos y valida cada uno con las reglas de CU-10, sin duplicarlas. Para la vista previa
// sólo cotiza; al confirmar crea cada envío en su propia transacción, así un error no revierte los demás. La importación
// es idempotente por la referencia del comercio (RF 7): reimportar el archivo informa los existentes como tales (A3).
internal sealed class ImportarEnviosHandler(IAmbitoPorEnvio ambito)
{
    public async Task<IReadOnlyList<EnvioImportado>> HandleAsync(ImportarEnviosCommand command, CancellationToken ct)
    {
        var resultados = new List<EnvioImportado>();

        // Paso 4. La referencia distingue mayúsculas y minúsculas, igual que en el envío.
        foreach (var grupo in command.Filas.GroupBy(f => f.Referencia.Trim(), StringComparer.Ordinal))
        {
            var filas = grupo.Select(f => f.Numero).ToList();
            try
            {
                var alta = ArmarEnvio(grupo.Key, grupo.ToList());
                resultados.Add(command.Confirmar
                    ? await CrearAsync(grupo.Key, filas, alta, ct)
                    : await PrevisualizarAsync(grupo.Key, filas, alta, ct));
            }
            catch (DomainException e)
            {
                resultados.Add(new EnvioImportado(grupo.Key, filas, ResultadoImportacion.Error, null, null, e.Message));
            }
        }

        return resultados;
    }

    private async Task<EnvioImportado> PrevisualizarAsync(string referencia, IReadOnlyList<int> filas,
        CrearEnvioCommand alta, CancellationToken ct)
    {
        var (existente, cotizacion) = await ambito.EjecutarAsync(h => h.PrevisualizarAsync(alta, ct), ct);
        return existente is not null
            ? new EnvioImportado(referencia, filas, ResultadoImportacion.YaExistente, existente.Numero, existente.MontoTarifa, null)
            : new EnvioImportado(referencia, filas, ResultadoImportacion.ACrear, null, cotizacion!.Total, null);
    }

    private async Task<EnvioImportado> CrearAsync(string referencia, IReadOnlyList<int> filas,
        CrearEnvioCommand alta, CancellationToken ct)
    {
        var resultado = await ambito.EjecutarAsync(h => h.CrearOObtenerAsync(alta, ct), ct);
        return new EnvioImportado(referencia, filas,
            resultado.Creado ? ResultadoImportacion.Creado : ResultadoImportacion.YaExistente,
            resultado.Envio.Numero, resultado.Envio.MontoTarifa, null);
    }

    // Las filas de un envío comparten destinatario, dirección y modalidad; cada una aporta un bulto.
    internal static CrearEnvioCommand ArmarEnvio(string referencia, IReadOnlyList<FilaImportacion> filas)
    {
        if (referencia.Length == 0)
            throw new DomainException($"{Filas(filas)} sin la referencia del comercio, que es obligatoria al importar.");

        // A5: la misma referencia con otro destinatario, otra dirección u otra modalidad no es el mismo envío.
        var primera = filas[0];
        if (filas.Any(f => DatosDelEnvio(f) != DatosDelEnvio(primera)))
            throw new DomainException($"{Filas(filas)} con la misma referencia, pero distinto destinatario, dirección o modalidad.");

        return new CrearEnvioCommand(
            new Destinatario(primera.Nombre, primera.Telefono, Opcional(primera.Email), Opcional(primera.Documento)),
            new Direccion(primera.Calle, primera.NumeroPuerta, primera.Localidad, primera.Departamento,
                primera.CodigoPostal.Trim(), Opcional(primera.ReferenciaDireccion)),
            filas.Select(f => new BultoACrear(
                Numero(f.PesoKg, f.Numero, "el peso"), Numero(f.LargoCm, f.Numero, "el largo"),
                Numero(f.AnchoCm, f.Numero, "el ancho"), Numero(f.AltoCm, f.Numero, "el alto"))).ToList(),
            OrigenEvento.PortalComercio,
            LeerModalidad(primera.Modalidad, primera.Numero),
            referencia);
    }

    private static string DatosDelEnvio(FilaImportacion f) => string.Join('\u001f',
        new[]
        {
            f.Nombre, f.Telefono, f.Email, f.Documento, f.Calle, f.NumeroPuerta, f.Localidad, f.Departamento,
            f.CodigoPostal, f.ReferenciaDireccion, f.Modalidad,
        }.Select(v => v.Trim()));

    private static string Filas(IReadOnlyList<FilaImportacion> filas) => filas.Count == 1
        ? $"La fila {filas[0].Numero} está"
        : $"Las filas {string.Join(", ", filas.Select(f => f.Numero))} están";

    private static string? Opcional(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    // Acepta coma o punto decimal: una planilla en español guarda «2,5».
    private static decimal Numero(string valor, int fila, string campo)
    {
        if (decimal.TryParse(valor.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var numero))
            return numero;
        throw new DomainException($"La fila {fila} tiene {campo} vacío o inválido: «{valor.Trim()}».");
    }

    private static Modalidad LeerModalidad(string valor, int fila) => valor.Trim().ToLowerInvariant() switch
    {
        "" or "estandar" or "estándar" => Modalidad.Estandar,
        "urgente" => Modalidad.Urgente,
        _ => throw new DomainException($"La fila {fila} tiene una modalidad inválida: usá Estándar o Urgente."),
    };
}
