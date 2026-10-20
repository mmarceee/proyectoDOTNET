namespace Logistica.Modules.Seguimiento.Application.Abstractions;

internal interface ISecretosAviso
{
    string Generar();
    string Cifrar(string secreto, Guid operadorId, Guid comercioId);
    string Descifrar(string secretoCifrado, Guid operadorId, Guid comercioId);
}
