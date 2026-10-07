namespace Logistica.Modules.Deposito.Domain.Recepciones;

// Peso y dimensiones de un bulto. Cada valor es opcional porque el operario puede medir sólo algunos
// (CU-30, paso 4); lo declarado por el comercio viene siempre completo.
internal sealed record Medidas(decimal? PesoKg, decimal? LargoCm, decimal? AnchoCm, decimal? AltoCm);
