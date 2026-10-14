using Logistica.Modules.Administracion.Application.Features.IniciarSesion;

namespace Logistica.Modules.Administracion.Presentation.Features.IniciarSesion;

internal sealed class IngresarRepartidorModel(IServicioSesion sesion)
    : IngresoModel(sesion, TipoAccesoSesion.Repartidor, "/repartidor/") { }
