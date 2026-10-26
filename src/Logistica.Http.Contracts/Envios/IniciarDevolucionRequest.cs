namespace Logistica.Http.Contracts.Envios;

// Cuerpo de POST /api/envios/{id}/devolucion (CU-20). El motivo es obligatorio; el Id del envío va en la URL.
public sealed record IniciarDevolucionRequest(string Motivo);
