using Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.GestionarFlota;

internal sealed record DatosVehiculo(string Tipo, decimal CapacidadPesoKg, decimal CapacidadVolumenM3,
    decimal LargoCargaCm, decimal AnchoCargaCm, decimal AltoCargaCm);

// CU-08: alta, modificación y desactivación de repartidores y vehículos. Coordina: las reglas de uno solo (A2) las
// validan Repartidor y Vehiculo; acá van las que comparan con otros datos (A1 con los demás del operador, A3 con las
// rutas de Planificación).
// Si el recurso no es del operador de la sesión, el filtro lo oculta y se devuelve false (como si no existiera).
internal sealed class GestionarFlotaHandler(
    IFlotaRepository flota, ICurrentTenant tenant, IPlanificacionModuleApi planificacion,
    IRecursosPlanificacionReader recursos, IUnidadDeTrabajo unidad)
{
    private Guid Operador => tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");

    public async Task<Guid> CrearRepartidorAsync(string nombre, string documento, CancellationToken ct)
    {
        var repartidor = Repartidor.Crear(Operador, nombre, documento);

        // A1: el documento es único en el operador. Ante dos altas simultáneas, el índice único frena la segunda y el
        // repositorio la informa con el mismo mensaje.
        if (await flota.ExisteDocumentoAsync(repartidor.Documento, ct))
        {
            throw new DomainException($"Ya existe un repartidor con el documento {repartidor.Documento}.");
        }

        flota.Agregar(repartidor);
        await flota.GuardarCambiosAsync(ct);
        return repartidor.Id;
    }

    public async Task<Guid> CrearVehiculoAsync(string matricula, DatosVehiculo datos, CancellationToken ct)
    {
        var vehiculo = Vehiculo.Crear(Operador, matricula, datos.Tipo, datos.CapacidadPesoKg, datos.CapacidadVolumenM3,
            datos.LargoCargaCm, datos.AnchoCargaCm, datos.AltoCargaCm);

        // A1: la matrícula es única en el operador.
        if (await flota.ExisteMatriculaAsync(vehiculo.Matricula, ct))
        {
            throw new DomainException($"Ya existe un vehículo con la matrícula {vehiculo.Matricula}.");
        }

        flota.Agregar(vehiculo);
        await flota.GuardarCambiosAsync(ct);
        return vehiculo.Id;
    }

    public async Task<bool> ModificarRepartidorAsync(Guid id, string nombre, CancellationToken ct)
    {
        if (await flota.ObtenerRepartidorAsync(id, ct) is not Repartidor repartidor)
        {
            return false;
        }

        repartidor.Modificar(nombre);
        await flota.GuardarCambiosAsync(ct);
        return true;
    }

    public async Task<bool> ModificarVehiculoAsync(Guid id, DatosVehiculo datos, CancellationToken ct)
    {
        if (await flota.ObtenerVehiculoAsync(id, ct) is not Vehiculo vehiculo)
        {
            return false;
        }

        vehiculo.Modificar(datos.Tipo, datos.CapacidadPesoKg, datos.CapacidadVolumenM3, datos.LargoCargaCm,
            datos.AnchoCargaCm, datos.AltoCargaCm);
        await flota.GuardarCambiosAsync(ct);
        return true;
    }

    // A3: no se desactiva mientras esté en una ruta que no se finalizó. Toma el mismo bloqueo de planificación que
    // armar y modificar rutas: nadie lo asigna a una ruta entre la comprobación y el guardado.
    public async Task<bool> DesactivarRepartidorAsync(Guid id, CancellationToken ct)
    {
        var desactivado = false;
        await unidad.EjecutarAsync(async () =>
        {
            await recursos.BloquearPlanificacionAsync(ct);
            if (await flota.ObtenerRepartidorAsync(id, ct) is not Repartidor repartidor)
            {
                return;
            }

            await ExigirSinRutaAsync(repartidorId: id, vehiculoId: null, ct);
            repartidor.Desactivar();
            await flota.GuardarCambiosAsync(ct);
            desactivado = true;
        }, ct);

        return desactivado;
    }

    public async Task<bool> DesactivarVehiculoAsync(Guid id, CancellationToken ct)
    {
        var desactivado = false;
        await unidad.EjecutarAsync(async () =>
        {
            await recursos.BloquearPlanificacionAsync(ct);
            if (await flota.ObtenerVehiculoAsync(id, ct) is not Vehiculo vehiculo)
            {
                return;
            }

            await ExigirSinRutaAsync(repartidorId: null, vehiculoId: id, ct);
            vehiculo.Desactivar();
            await flota.GuardarCambiosAsync(ct);
            desactivado = true;
        }, ct);

        return desactivado;
    }

    // Las rutas son de Planificación: se le pregunta por su contrato (ADR-0001, addendum 3).
    private async Task ExigirSinRutaAsync(Guid? repartidorId, Guid? vehiculoId, CancellationToken ct)
    {
        if (await planificacion.HayRutaSinFinalizarConAsync(repartidorId, vehiculoId, ct))
        {
            throw new DomainException("Está asignado a una ruta que todavía no se finalizó. Desactivalo cuando termine.");
        }
    }
}
