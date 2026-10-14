using Logistica.Modules.Administracion.Application.Features.IniciarSesion;

namespace Logistica.Modules.Administracion.Presentation.Features.IniciarSesion;

internal sealed class IngresarPortalModel(IServicioSesion sesion)
    : IngresoModel(sesion, TipoAccesoSesion.Comercio, "/portal/") { }
