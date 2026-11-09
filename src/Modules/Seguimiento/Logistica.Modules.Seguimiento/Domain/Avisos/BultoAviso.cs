namespace Logistica.Modules.Seguimiento.Domain.Avisos;

// RF 36: el bulto de un aviso v2. Codigo es el de la etiqueta; Estado, el del bulto después del hecho; Motivo, el
// código del motivo de no entrega, si lo hay.
internal sealed record BultoAviso(string Codigo, string Estado, string? Motivo = null)
{
    public const int LargoCodigo = 60;
    public const int LargoEstado = 30;
    public const int LargoMotivo = 50;
}
