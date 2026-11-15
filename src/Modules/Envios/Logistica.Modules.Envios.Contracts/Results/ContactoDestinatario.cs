namespace Logistica.Modules.Envios.Contracts.Results;

// CU-61: por dónde comprobar que quien reprograma desde el seguimiento público es el destinatario. Nunca se muestra
// completo: Seguimiento manda el código al correo, o pide los últimos dígitos del teléfono.
public sealed record ContactoDestinatario(string? Email, string Telefono);
