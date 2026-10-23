namespace Logistica.Modules.Envios.Application.Features.DeclararExtravio;

// Lo que hace falta para declarar extraviado un envío, ya traducido desde la página (CU-21). El envío se identifica por
// su número, como en el detalle del Backoffice. El motivo es obligatorio (paso 2); el responsable es el usuario de la
// sesión, que lo toma la página de sus claims.
internal sealed record DeclararExtravioCommand(string Numero, string Motivo, Guid? ResponsableId);
