namespace Logistica.SharedKernel;

// Se viola una regla de negocio, por ejemplo un envio sin bultos o una transicion no permitida.
public class DomainException(string message) : Exception(message);
