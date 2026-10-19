using Logistica.Http.Contracts.Repartidor;
using Logistica.Repartidor.Pwa.Services;
using Microsoft.JSInterop;

namespace Logistica.IntegrationTests.Ejecucion;

public sealed class CargaServiceTests
{
    [Fact]
    public async Task Abrir_sin_red_espera_la_sincronizacion_inicial_y_recupera_el_borrador()
    {
        var modulo = new Modulo();
        using var http = new HttpClient(new SinServidor()) { BaseAddress = new Uri("https://localhost") };
        await using var hoja = new HojaDeRutaService(http, modulo);
        await using var cargas = new CargaService(modulo, hoja);
        var sincronizando = cargas.SincronizarAsync();
        await modulo.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var abriendo = cargas.AbrirAsync();
        Assert.False(abriendo.IsCompleted); // No descartar la apertura porque la sincronización tenga el semáforo.
        modulo.Continuar.SetResult();
        await Task.WhenAll(sincronizando,abriendo).WaitAsync(TimeSpan.FromSeconds(5));
        await cargas.SincronizarAsync();
        Assert.True(hoja.SinConexion);
        Assert.NotNull(cargas.Borrador);
        Assert.Equal(Modulo.Ruta,cargas.Borrador.RutaId);
        Assert.Single(cargas.Borrador.Lecturas);
    }
    private sealed class SinServidor : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
            => throw new HttpRequestException("API detenida");
    }
    private sealed class Modulo : IJSRuntime,IJSObjectReference
    {
        public static readonly Guid Ruta=Guid.NewGuid();
        public readonly TaskCompletionSource Iniciada=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Continuar=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int sincronizaciones;
        private static readonly SesionRepartidorResponse Sesion=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,10,19),"America/Montevideo",DateTimeOffset.UtcNow);
        private static readonly HojaDeRutaResponse Documento=new(1,Sesion.UsuarioId,Sesion.OperadorId,Sesion.RepartidorId,Ruta,2,Sesion.FechaOperativa,Sesion.FechaOperativa,Sesion.ZonaHorariaOperador,"Despachada",DateTimeOffset.UtcNow,new(Guid.NewGuid(),"CU51"),[],[]);
        public ValueTask<T> InvokeAsync<T>(string id,object?[]? args)=>InvokeAsync<T>(id,CancellationToken.None,args);
        public async ValueTask<T> InvokeAsync<T>(string id,CancellationToken ct,object?[]? args)
        {
            if(id=="sincronizarCargas")
            {
                if(Interlocked.Increment(ref sincronizaciones)==1) { Iniciada.SetResult(); await Continuar.Task; }
                return (T)(object)new ResumenSincronizacion(null,0);
            }
            object? valor=id switch
            {
                "import"=>this,
                "sesionLocal"=>Sesion,
                "cargar"=>new CopiaHoja(Documento,DateTimeOffset.UtcNow,false),
                "abrirCarga" or "borradorCarga"=>new BorradorCarga(Ruta,2,2,[new("ENV-PRUEBA-1",DateTimeOffset.UtcNow)],null,null,null),
                _=>null
            };
            return valor is null ? default! : (T)valor;
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
