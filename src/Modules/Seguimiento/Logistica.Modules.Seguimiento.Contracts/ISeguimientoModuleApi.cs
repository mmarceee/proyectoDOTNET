namespace Logistica.Modules.Seguimiento.Contracts;

// Lo que Seguimiento ofrece a los demás módulos, en memoria (ADR-0001, addendum 3).
public interface ISeguimientoModuleApi
{
    // CU-60: el enlace público de seguimiento de un envío del operador de la sesión ("/seguimiento/{token}"). El token
    // está firmado y lleva el operador y el envío (ADR-0002, sección 2.5): no se puede adivinar ni alterar.
    string EnlaceDe(Guid envioId);
}
