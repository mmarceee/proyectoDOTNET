using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// RF 33: el cargo fijo de una visita adicional del envío, registrado una sola vez al realizarla, aunque falle
// (decisiones-entrega-parcial.md, sección 5). Guarda el monto de ese momento: la liquidación lo cobra tal cual y no lo
// recalcula si después cambian las reglas (CU-32).
internal sealed class RecargoNuevaVisita : Entity, IOperadorOwned, IComercioOwned
{
    public Guid EnvioId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public decimal Monto { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }

    private RecargoNuevaVisita() { } // para EF Core

    // Sólo lo crea el envío, al registrar la visita (Envio.RegistrarVisita).
    internal RecargoNuevaVisita(Envio envio, decimal monto, DateTimeOffset ocurridoEn)
    {
        if (monto <= 0)
        {
            throw new DomainException("El recargo de la nueva visita tiene que ser mayor que cero.");
        }

        EnvioId = envio.Id;
        OperadorId = envio.OperadorId;
        ComercioId = envio.ComercioId;
        Monto = monto;
        // En UTC, como el resto de las fechas: PostgreSQL no admite otro desfase.
        OcurridoEn = ocurridoEn.ToUniversalTime();
    }
}
