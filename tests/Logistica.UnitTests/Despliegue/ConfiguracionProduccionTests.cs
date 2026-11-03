using Logistica.BuildingBlocks.Infrastructure.Configuracion;
using Logistica.Modules.Administracion.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Logistica.UnitTests.Despliegue;

// Lo que la API y el Worker exigen para arrancar en producción y no en desarrollo.
public class ConfiguracionProduccionTests
{
    [Fact]
    public void En_produccion_no_arranca_sin_Valkey_ni_RabbitMQ()
    {
        var configuracion = Configuracion(("ConnectionStrings:Postgres", "Host=db"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            ConexionesObligatorias.Exigir(configuracion, Entorno(Environments.Production), "Postgres", "Valkey", "RabbitMQ"));

        Assert.Contains("Valkey, RabbitMQ", error.Message);
    }

    [Fact]
    public void En_produccion_arranca_con_todas_las_conexiones_y_en_desarrollo_sin_ninguna()
    {
        var completa = Configuracion(("ConnectionStrings:Postgres", "Host=db"), ("ConnectionStrings:Valkey", "valkey:6379"),
            ("ConnectionStrings:RabbitMQ", "amqp://rabbitmq"));

        ConexionesObligatorias.Exigir(completa, Entorno(Environments.Production), "Postgres", "Valkey", "RabbitMQ");
        ConexionesObligatorias.Exigir(Configuracion(), Entorno(Environments.Development), "Postgres", "Valkey", "RabbitMQ");
    }

    [Fact]
    public void La_contrasena_de_demostracion_sale_de_la_configuracion_y_en_produccion_es_obligatoria()
    {
        var conSecreto = Configuracion(("Demo:Contrasena", "Otra-Clave-2026!"));

        Assert.Equal("Otra-Clave-2026!", UsuariosIniciales.ContrasenaInicial(conSecreto, Entorno(Environments.Production)));
        Assert.Equal(UsuariosIniciales.ContrasenaDemo,
            UsuariosIniciales.ContrasenaInicial(Configuracion(), Entorno(Environments.Development)));
        Assert.Throws<InvalidOperationException>(() =>
            UsuariosIniciales.ContrasenaInicial(Configuracion(), Entorno(Environments.Production)));
    }

    private static IConfiguration Configuracion(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Clave, v.Valor)))
            .Build();

    private static IHostEnvironment Entorno(string nombre) => new EntornoFijo { EnvironmentName = nombre };

    private sealed class EntornoFijo : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "Logistica.UnitTests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
