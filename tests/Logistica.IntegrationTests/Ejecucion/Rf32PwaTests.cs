using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Repartidor;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Domain.Reglas;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Ejecucion;

public sealed class Rf32PwaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid Operador = DatosIniciales.OperadorDemoId;
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD8kAAAAASUVORK5CYII=";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task La_carga_y_visita_por_bulto_conservan_el_tercero_y_no_duplican_efectos(bool todosCargados, bool cargaEnEtapas)
    {
        using var scope = factory.Services.CreateScope();
        await using var admin = Contexto<AdministracionDbContext>(scope);
        var baseReglas = await admin.VersionesReglas.Include(v => v.Motivos).OrderBy(v => v.Numero).FirstAsync();
        var numero = await admin.VersionesReglas.MaxAsync(v => v.Numero) + 1;
        var valores = baseReglas.Valores() with { EntregaParcial = new ReglasEntregaParcial(true, PoliticaPendientes.NuevaVisitaConRecargo, 150) };
        var version = VersionReglas.CrearBorrador(Operador, numero, valores, baseReglas.Motivos.Select(m => m.Datos()));
        var ahora = DateTimeOffset.UtcNow;
        var previa = await admin.VersionesReglas.OrderByDescending(v => v.Numero).FirstAsync();
        version.Publicar(ahora, ahora, previa);
        admin.VersionesReglas.Add(version); await admin.SaveChangesAsync();
        var envio = Envio.Crear(Operador, DatosIniciales.ComercioDemoId, $"ENV-{Guid.NewGuid():N}",
            new Destinatario("Ana RF32", "099123456"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            Enumerable.Repeat(new DatosBulto(1, 10, 10, 10, 100), 3).ToArray(), OrigenEvento.PortalComercio, null, ahora,
            versionReglasId: version.Id);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
        await using var db = Contexto<EnviosDbContext>(scope);
        db.Envios.Add(envio); await db.SaveChangesAsync();
        var fecha = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo")).DateTime);
        var ruta = Ruta.Crear(Operador, fecha, DatosIniciales.RepartidorDemoId, Guid.Parse("51111111-1111-1111-1111-111111111111"),
            [new EnvioRuta(envio.Id, envio.Numero, null, null, null, false, [])], ahora);
        ruta.Despachar(ahora);
        await using var plan = Contexto<PlanificacionDbContext>(scope);
        plan.Rutas.Add(ruta); await plan.SaveChangesAsync();
        await DatosFisicosRf34.AcreditarAsync(db, plan, ruta, [envio], ahora);
        using var cliente = factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(cliente, "repartidor@operador-demo.uy");
        var codigos = envio.Bultos.Select(b => b.Codigo).ToArray();
        var cargados = todosCargados ? codigos : codigos.Take(2).ToArray();
        var primeraCarga = cargaEnEtapas ? codigos.Take(1).ToArray() : cargados;
        var dispositivo = Guid.NewGuid();
        var carga = new OperacionDispositivoRequest(Guid.CreateVersion7(), dispositivo, 1, "ConfirmarCarga", ruta.Id, null,
            ahora, null, null, Carga:new(ruta.Revision, primeraCarga.Select(c => new LecturaCargaRequest(c, ahora)).ToArray(), !todosCargados || cargaEnEtapas,
                PorBulto:true, Faltantes:codigos.Except(primeraCarga).Select(c => new FaltanteCargaRequest(c, "NoLocalizado")).ToArray()));
        Assert.Equal("Aplicada", (await Enviar(cliente,carga)).Resultado);
        Assert.Equal("YaAplicada", (await Enviar(cliente,carga)).Resultado);
        if (cargaEnEtapas)
        {
            var intermedia = await cliente.GetFromJsonAsync<HojaDeRutaResponse>($"/api/repartidor/rutas/{ruta.Id}/hoja-de-ruta");
            Assert.NotNull(intermedia);
            Assert.Equal("Despachada", intermedia.EstadoRuta);
            var bultosIntermedios = Assert.Single(Assert.Single(intermedia.Paradas).Envios).Bultos;
            Assert.Equal(3, bultosIntermedios.Count);
            Assert.Equal(2, bultosIntermedios.Count(b => b.Estado == "AsignadoARuta"));
            Assert.Single(bultosIntermedios, b => b.Estado == "EnTransito");
            var segundaCarga = carga with { OperacionId = Guid.CreateVersion7(), Secuencia = 2,
                Carga = new(intermedia.RevisionRuta, codigos.Skip(1).Select(c => new LecturaCargaRequest(c, ahora)).ToArray(),
                    PorBulto:true, Faltantes:[]) };
            Assert.Equal("Aplicada", (await Enviar(cliente,segundaCarga)).Resultado);
            Assert.Equal("YaAplicada", (await Enviar(cliente,segundaCarga)).Resultado);
        }
        var inicio = new OperacionDispositivoRequest(Guid.CreateVersion7(), dispositivo, cargaEnEtapas ? 3 : 2, "IniciarRuta", ruta.Id, null,
            ahora, null, null);
        Assert.Equal("Aplicada", (await Enviar(cliente,inicio)).Resultado);
        Assert.Equal("YaAplicada", (await Enviar(cliente,inicio)).Resultado);
        db.ChangeTracker.Clear();
        var cargado = await db.Envios.Include(e => e.Intentos).SingleAsync(e => e.Id == envio.Id);
        Assert.Equal(todosCargados ? EstadoEnvio.EnTransito : EstadoEnvio.AsignadoARuta, cargado.Bultos.Single(b => b.Codigo == codigos[2]).Estado);
        Assert.Empty(cargado.Intentos);
        var foto = new ArchivoRequest("image/png", Png);
        var motivo = version.Motivos.First(m => m.Codigo == "AUSENTE").Id;
        var resultados = cargados.Select((c,i) => new ResultadoBultoRequest(c, i == 2 ? motivo : null)).ToArray();
        var visita = new OperacionDispositivoRequest(Guid.CreateVersion7(),dispositivo,cargaEnEtapas ? 4 : 3,"RegistrarVisita",ruta.Id,envio.Id,
            ahora.AddSeconds(1),-34.9m,-56.16m,Visita:new(resultados,foto,foto));
        Assert.Equal("Aplicada",(await Enviar(cliente,visita)).Resultado);
        Assert.Equal("YaAplicada",(await Enviar(cliente,visita)).Resultado);
        db.ChangeTracker.Clear();
        var final = await db.Envios.Include(e => e.Intentos).Include(e => e.Recargos).SingleAsync(e => e.Id == envio.Id);
        Assert.Equal(ResumenEnvio.EntregadoParcialmente,final.Resumen); Assert.Equal(2,final.BultosEntregados); Assert.False(final.Terminado);
        Assert.Empty(final.Recargos);
        var exitoso = Assert.Single(final.Intentos, i=>i.Resultado==ResultadoIntento.Exitoso);
        Assert.Equal(2,exitoso.BultoIds.Length);
        if (todosCargados)
        {
            var fallido=Assert.Single(final.Intentos, i=>i.Resultado==ResultadoIntento.Fallido);
            Assert.Equal(exitoso.Evidencia!.FotoArchivoId,fallido.Evidencia!.FotoArchivoId);
            Assert.Null(fallido.Evidencia.FirmaArchivoId); Assert.Single(fallido.BultoIds);
        }
        else Assert.Equal(0,final.Bultos.Single(b=>b.Codigo==codigos[2]).IntentosFallidos);
        Assert.False(await db.Outbox.AnyAsync(o=>o.Tipo=="EnvioEntregado.v1" && o.Contenido.Contains(envio.Id.ToString())));
        var hoja=await cliente.GetFromJsonAsync<HojaDeRutaResponse>($"/api/repartidor/rutas/{ruta.Id}/hoja-de-ruta");
        Assert.Equal(3,hoja!.SchemaVersion);var fila=Assert.Single(Assert.Single(hoja.Paradas).Envios);
        Assert.True(fila.ReglasEntrega.AdmiteEntregaParcial); Assert.All(fila.Bultos,b=>Assert.NotEmpty(b.Estado));
        Assert.Equal(codigos.Order(), fila.Bultos.Select(b => b.Codigo).Order());
        Assert.Equal(fila.Bultos.Select(b => b.Id).Order(), hoja.Rendicion!.Bultos.Select(b => b.BultoId).Order());
        await using var ejecucion=Contexto<EjecucionDbContext>(scope);
        Assert.Equal(1,await ejecucion.Operaciones.CountAsync(o=>o.Id==visita.OperacionId));
        plan.ChangeTracker.Clear();
        var cerrada = await plan.Rutas.Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleAsync(r => r.Id == ruta.Id);
        cerrada.Finalizar(); await plan.SaveChangesAsync();
        if (todosCargados)
        {
            // Una nueva visita real trae su propia foto y sólo respalda el tercero. Su reenvío no duplica el recargo.
            var pendienteFinal = final.Bultos.Single(b => b.Codigo == codigos[2]);
            var reservaAnterior = await plan.ReservasBulto.SingleAsync(r => r.RutaId == ruta.Id && r.BultoId == pendienteFinal.Id && r.CerradaEn == null);
            pendienteFinal.ConfirmarCustodia(UbicacionCustodia.Deposito, null, null, Guid.NewGuid(), ahora.AddSeconds(2), "Retorno físico antes de la revisita");
            reservaAnterior.Cerrar("RetornoConfirmado", ahora.AddSeconds(2));
            await plan.SaveChangesAsync();
            final.Transicionar(EstadoEnvio.AsignadoARuta,OrigenEvento.Backoffice,null,ahora.AddSeconds(2));
            final.Transicionar(EstadoEnvio.EnTransito,OrigenEvento.AppRepartidor,null,ahora.AddSeconds(2));
            await db.SaveChangesAsync();
            var revisita = Ruta.Crear(Operador,fecha,DatosIniciales.RepartidorDemoId,Guid.Parse("51111111-1111-1111-1111-111111111111"),
                [new EnvioRuta(envio.Id,envio.Numero,null,null,null,false,[])],ahora.AddSeconds(2));
            revisita.Despachar(ahora.AddSeconds(2));revisita.Iniciar();plan.Rutas.Add(revisita);await plan.SaveChangesAsync();
            await DatosFisicosRf34.AcreditarAsync(db, plan, revisita, [final], ahora.AddSeconds(2), [pendienteFinal.Id]);
            var hojaRevisita = await cliente.GetFromJsonAsync<HojaDeRutaResponse>($"/api/repartidor/rutas/{revisita.Id}/hoja-de-ruta");
            Assert.NotNull(hojaRevisita);
            Assert.Equal(codigos[2], Assert.Single(Assert.Single(Assert.Single(hojaRevisita.Paradas).Envios).Bultos).Codigo);
            var otra = visita with { OperacionId=Guid.CreateVersion7(), Secuencia=visita.Secuencia + 1, RutaId=revisita.Id,
                OcurridoEn=ahora.AddSeconds(3), Visita=new([new(codigos[2])],foto,foto) };
            Assert.Equal("Aplicada",(await Enviar(cliente,otra)).Resultado);
            Assert.Equal("YaAplicada",(await Enviar(cliente,otra)).Resultado);
            db.ChangeTracker.Clear();
            var terminado=await db.Envios.Include(e=>e.Intentos).Include(e=>e.Recargos).SingleAsync(e=>e.Id==envio.Id);
            Assert.Equal(ResumenEnvio.Entregado,terminado.Resumen);
            Assert.Equal(150m,Assert.Single(terminado.Recargos).Monto);
            var tercero=Assert.Single(terminado.Intentos,i=>i.Resultado==ResultadoIntento.Exitoso && i.BultoIds.Length==1);
            Assert.NotEqual(exitoso.Evidencia!.FotoArchivoId,tercero.Evidencia!.FotoArchivoId);
            Assert.Equal(envio.Bultos[2].Id,Assert.Single(tercero.BultoIds));
            plan.ChangeTracker.Clear();
            var segundaCerrada = await plan.Rutas.Include(r=>r.Paradas).ThenInclude(p=>p.Envios).SingleAsync(r=>r.Id==revisita.Id);
            segundaCerrada.Finalizar();await plan.SaveChangesAsync();
        }
    }
    private static async Task<ResultadoOperacionResponse> Enviar(HttpClient cliente,OperacionDispositivoRequest op)
    {
        using var r=await cliente.PostAsJsonAsync("/api/repartidor/sincronizacion",new SincronizacionRequest([op]));r.EnsureSuccessStatusCode();
        return Assert.Single((await r.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados);
    }
    private static T Contexto<T>(IServiceScope scope) where T:Logistica.BuildingBlocks.Infrastructure.Persistence.ModuleDbContext =>
        ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider,new InquilinoFijo(Operador,null));
}
