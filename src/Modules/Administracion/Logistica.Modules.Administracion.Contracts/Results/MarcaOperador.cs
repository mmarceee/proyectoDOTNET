namespace Logistica.Modules.Administracion.Contracts.Results;

// La identidad visual del operador (CU-07) para pantallas sin sesión, como el seguimiento público (CU-60).
public sealed record MarcaOperador(string Nombre, string ColorPrimario, string ColorSecundario, string? EmailContacto,
    string? TelefonoContacto);
