using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Administracion.Domain.Reglas;

// El plazo comprometido de entrega para una modalidad, en horas (CU-06). Lo usan el alta (CU-10) y la ventana estimada
// del seguimiento (CU-60).
internal sealed record PlazoModalidad(Modalidad Modalidad, int PlazoHoras);
