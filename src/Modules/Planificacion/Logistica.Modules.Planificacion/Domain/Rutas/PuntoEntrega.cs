using System.Text.RegularExpressions;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

// El apartamento y la referencia pertenecen al envío: no separan visitas al edificio.
internal sealed record PuntoEntrega(string Calle, string Numero, string Localidad, string Departamento, string CodigoPostal)
{
    public string Clave => string.Join("|", new[] { Calle, Numero, Localidad, Departamento, CodigoPostal }
        .Select(v => Regex.Replace(v.Trim(), @"\s+", " ").ToUpperInvariant()));
    public string Direccion => $"{Calle} {Numero}, {Localidad}";
}
