namespace Logistica.SharedKernel;

// Caché distribuida para las lecturas frecuentes, como el seguimiento público y el cuadro tarifario. La implementa
// BuildingBlocks.Infrastructure sobre Valkey, así los casos de uso no conocen el proveedor.
// El operador de la sesión forma parte de la clave: un operador nunca lee lo que guardó otro (ADR-0002). Si el dato
// depende también del comercio, el comercio va en la clave que arma el caso de uso.
public interface ICacheDistribuida
{
    // Devuelve el valor guardado o, si no está, lo calcula con crear y lo guarda durante la duración indicada.
    // cache nombra el grupo de datos ("seguimiento", "tarifario") y es la etiqueta de las métricas de aciertos;
    // clave identifica el dato dentro del grupo. Un null no se guarda.
    Task<T> ObtenerOCrearAsync<T>(string cache, string clave, Func<CancellationToken, Task<T>> crear,
        TimeSpan duracion, CancellationToken ct);

    // Borra el dato para que la próxima lectura lo vuelva a calcular. Se llama después de guardar el cambio.
    Task InvalidarAsync(string cache, string clave, CancellationToken ct);
}
