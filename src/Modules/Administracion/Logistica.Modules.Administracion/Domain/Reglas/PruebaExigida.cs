namespace Logistica.Modules.Administracion.Domain.Reglas;

// Las pruebas de entrega que se exigen además de la firma, que es siempre obligatoria y no se configura (CU-06; CU-17).
internal sealed record PruebaExigida(bool ExigeFoto, bool ExigeNombreYDocumento);
