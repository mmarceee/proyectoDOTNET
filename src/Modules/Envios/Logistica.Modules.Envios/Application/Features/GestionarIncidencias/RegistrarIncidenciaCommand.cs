using Logistica.Modules.Envios.Domain.Incidencias;

namespace Logistica.Modules.Envios.Application.Features.GestionarIncidencias;

// CU-22, paso 2: la incidencia nueva, ya traducida desde la página. El envío se identifica por su número, como en el
// detalle del Backoffice.
internal sealed record RegistrarIncidenciaCommand(string Numero, TipoIncidencia Tipo, string Descripcion);
