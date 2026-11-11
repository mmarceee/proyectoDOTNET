namespace Logistica.Modules.Envios.Application.Features.ImprimirEtiquetas;

// Sólo los datos imprimibles: nunca teléfono, documento ni token de seguimiento.
internal sealed record EnvioParaEtiquetas(string Numero, bool Cancelado, string Destinatario,
    string Direccion, Guid? ZonaId, string Modalidad, int TotalBultos, IReadOnlyList<string> Codigos);

internal sealed record EtiquetaBulto(string NumeroEnvio, string Codigo, int Posicion, int Total,
    string Destinatario, string Direccion, string Zona, string Modalidad);

internal sealed record MarcaEtiquetas(string Nombre, string? TipoLogo, byte[]? Logo);

internal interface IEtiquetasReader
{
    Task<IReadOnlyList<EnvioParaEtiquetas>> ConsultarAsync(string[] numeros, CancellationToken ct);
}

internal interface IGeneradorEtiquetas
{
    byte[] Generar(MarcaEtiquetas marca, IReadOnlyList<EtiquetaBulto> etiquetas);
}

internal sealed record ResultadoEtiquetas(byte[]? Pdf, string? Error = null, bool NoEncontrado = false);
