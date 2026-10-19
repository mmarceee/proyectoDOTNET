using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Ejecucion.Application.Features.DescargarHojaDeRuta;

internal sealed class HojaIncompletaException() : Exception("No se pudo construir una hoja de ruta completa.");
internal sealed class VinculoRepartidorInvalidoException() : Exception("La cuenta no está vinculada a un repartidor activo.");

internal sealed class DescargarHojaDeRutaHandler(IConsultaConsistente lectura, IContextoHojaRuta administracion,
    IHojaRutaPlanificacion planificacion, IEnviosHojaRuta envios, TimeProvider reloj)
{
    public Task<SesionRepartidorResponse> SesionAsync(Guid usuario, Guid repartidor, CancellationToken ct)
        => lectura.LeerAsync(async () =>
        {
            var contexto = await administracion.ConsultarAsync(usuario, repartidor, null, ct)
                ?? throw new VinculoRepartidorInvalidoException();
            return CrearSesion(usuario, repartidor, contexto);
        }, ct);

    public Task<HojaDeRutaResponse?> HandleAsync(Guid usuario, Guid repartidor, CancellationToken ct)
        => lectura.LeerAsync<HojaDeRutaResponse?>(async () =>
        {
            var contexto = await administracion.ConsultarAsync(usuario, repartidor, null, ct)
                ?? throw new VinculoRepartidorInvalidoException();
            var sesion = CrearSesion(usuario, repartidor, contexto);
            var ruta = await planificacion.ConsultarAsync(repartidor, sesion.FechaOperativa, ct);
            if (ruta is null) return null;
            contexto = await administracion.ConsultarAsync(usuario, repartidor, ruta.VehiculoId, ct)
                ?? throw new VinculoRepartidorInvalidoException();
            if (string.IsNullOrWhiteSpace(contexto.Matricula) || ruta.Paradas.Count == 0) throw new HojaIncompletaException();
            var ids = ruta.Paradas.SelectMany(p => p.Envios).ToArray();
            if (ids.Length == 0 || ids.Distinct().Count() != ids.Length
                || ruta.Paradas.Any(p => p.Envios.Count == 0)
                || ruta.Paradas.Select(p => p.Orden).Distinct().Count() != ruta.Paradas.Count)
                throw new HojaIncompletaException();
            var lista = await envios.ConsultarAsync(ids, ct);
            if (!lista.Select(e => e.Id).Order().SequenceEqual(ids.Order())) throw new HojaIncompletaException();
            var porId = lista.ToDictionary(e => e.Id);
            var zonas = new Dictionary<Guid, ZonaHojaResponse>();
            var bultos = new HashSet<Guid>();
            EnvioHojaResponse Mapear(EnvioParaHoja e)
            {
                var coincidencias = contexto.Zonas.Where(z => e.ZonaId is Guid zonaId
                    ? z.Id == zonaId : z.CodigosPostales.Contains(e.CodigoPostal)).ToArray();
                if (coincidencias.Length != 1 || e.Bultos.Count == 0 || e.Bultos.Any(b => !bultos.Add(b.Id))
                    || e.Reglas.Motivos.Count == 0 || e.Reglas.MaxIntentos < 1)
                    throw new HojaIncompletaException();
                var zona = coincidencias[0];
                zonas[zona.Id] = new(zona.Id, zona.Codigo, zona.Nombre);
                FranjaHojaResponse? franja = null;
                if (e.FranjaId is Guid franjaId)
                {
                    var f = contexto.Franjas.SingleOrDefault(f => f.Id == franjaId);
                    var visitadas = new HashSet<Guid>();
                    while (f is not null && !(ruta.Fecha >= f.VigenteDesde && (f.VigenteHasta is null || ruta.Fecha <= f.VigenteHasta)
                        && f.Dias.Contains(ruta.Fecha.DayOfWeek)))
                    {
                        if (!visitadas.Add(f.Id)) throw new HojaIncompletaException();
                        var anterior = f.Id;
                        f = contexto.Franjas.SingleOrDefault(s => s.ReemplazaAId == anterior);
                    }
                    if (f is null || f.ZonaId != zona.Id) throw new HojaIncompletaException();
                    franja = new(f.Id, f.Desde, f.Hasta);
                }
                var r = e.Reglas;
                return new(e.Id, e.Numero, e.Estado, e.Nombre, e.Telefono,
                    new(e.Calle, e.NumeroPuerta, e.Localidad, e.Departamento, e.CodigoPostal, e.Referencia),
                    franja, e.Bultos.Select(b => new BultoHojaResponse(b.Id, b.Codigo)).ToArray(),
                    new(r.Origen, r.VersionId, true, r.ExigeFoto, r.ExigeNombreYDocumento, r.MaxIntentos, r.PlazoEntreIntentos,
                        r.Motivos.Select(m => new MotivoHojaResponse(m.Id, m.Codigo, m.Nombre, m.Reprogramable, m.ExigeEvidencia)).ToArray()));
            }
            var paradas = ruta.Paradas.OrderBy(p => p.Orden).Select(p => new ParadaHojaResponse(p.Id, p.Orden, p.Estado,
                p.Direccion, p.Desde, p.Hasta, p.Envios.Select(id => Mapear(porId[id])).ToArray())).ToArray();
            return new(1, usuario, contexto.OperadorId, repartidor, ruta.Id, ruta.Revision, ruta.Fecha, sesion.FechaOperativa,
                contexto.ZonaHoraria, ruta.Estado, reloj.GetUtcNow(), new(ruta.VehiculoId, contexto.Matricula),
                zonas.Values.OrderBy(z => z.Nombre).ToArray(), paradas);
        }, ct);

    private SesionRepartidorResponse CrearSesion(Guid usuario, Guid repartidor, ContextoHojaRuta contexto)
    {
        var ahora = reloj.GetUtcNow();
        var dia = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, TimeZoneInfo.FindSystemTimeZoneById(contexto.ZonaHoraria)).DateTime);
        return new(usuario, contexto.OperadorId, repartidor, dia, contexto.ZonaHoraria, ahora);
    }
}
