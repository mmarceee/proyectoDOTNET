// Las clases corren en paralelo, hasta cuatro a la vez (los runners de GitHub tienen cuatro núcleos); las pruebas de una
// misma clase siguen en serie, sobre su propia base. Todas comparten un solo PostgreSQL (PostgresApiFactory), así que el
// límite ya no lo ponen los contenedores sino la CPU de los hosts de API mientras se recolecta cobertura.
[assembly: Xunit.CollectionBehavior(MaxParallelThreads = 4)]

namespace Logistica.IntegrationTests;

// Las clases que levantan contenedores propios (RabbitMQ, Valkey, Mailpit o un PostgreSQL aparte) esperan mensajes con
// plazos de 30 a 45 s o miden tiempos. Corren solas, después de las demás, para que la carga no las haga fallar.
[Xunit.CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ContenedoresPropios
{
    public const string Nombre = "Contenedores propios";
}
