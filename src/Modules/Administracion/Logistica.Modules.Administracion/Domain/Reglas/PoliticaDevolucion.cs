namespace Logistica.Modules.Administracion.Domain.Reglas;

// Qué pasa con un envío que agotó sus intentos (CU-06; CU-18). Preliminar: la letra no define su contenido exacto
// (modelo de dominio, PoliticaDevolucion).
internal sealed record PoliticaDevolucion(bool DevolverTrasIntentosAgotados, int PlazoDevolucionDias);
