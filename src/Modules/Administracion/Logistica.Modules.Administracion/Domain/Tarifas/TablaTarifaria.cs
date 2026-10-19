using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Administracion.Domain.Tarifas;

// Una versión publicada, reducida a lo necesario para calcular precios: sus reglas y sus ajustes como valores. Es lo que
// se guarda en la caché (ADR-0006, sección 2.4): una versión publicada no cambia, así que la copia nunca queda vieja.
// El cálculo usa las mismas reglas de ReglaTarifa y AjusteTarifa, sin repetirlas.
internal sealed record TablaTarifaria(
    Guid VersionId, int Numero, IReadOnlyList<DatosReglaTarifa> Reglas, IReadOnlyList<DatosAjusteTarifa> Ajustes)
{
    // El precio de un bulto: el de la regla que le corresponde más sus ajustes, nunca menos de cero (D2). null si ninguna
    // regla lo cubre (CU-10, A3).
    public decimal? Calcular(Guid zonaId, Modalidad modalidad, decimal pesoKg, decimal volumenM3)
    {
        var regla = Reglas.Select(datos => new ReglaTarifa(Guid.Empty, VersionId, datos))
            .FirstOrDefault(r => r.Aplica(zonaId, modalidad, pesoKg, volumenM3));
        if (regla is null)
        {
            return null;
        }

        var precio = regla.PrecioBase + Ajustes
            .Select(datos => new AjusteTarifa(Guid.Empty, VersionId, datos))
            .Sum(a => a.ImporteSobre(regla.PrecioBase, modalidad));
        return Math.Round(Math.Max(precio, 0), 2);
    }
}
