using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

internal sealed class Bulto : Entity, IOperadorOwned, IComercioOwned
{
    public Guid EnvioId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Codigo { get; private set; } = "";
    public decimal PesoKg { get; private set; }
    public decimal LargoCm { get; private set; }
    public decimal AnchoCm { get; private set; }
    public decimal AltoCm { get; private set; }
    public decimal MontoTarifa { get; private set; }

    private Bulto() { } // para EF Core

    // Sólo lo llama Envio: el bulto copia el inquilino de su envío.
    public Bulto(Envio envio, string codigo, DatosBulto datos)
    {
        if (datos.PesoKg <= 0 || datos.LargoCm <= 0 || datos.AnchoCm <= 0 || datos.AltoCm <= 0)
        {
            throw new DomainException("El peso y las medidas del bulto deben ser mayores a cero.");
        }

        if (datos.MontoTarifa < 0)
        {
            throw new DomainException("La tarifa del bulto no puede ser negativa.");
        }

        EnvioId = envio.Id;
        OperadorId = envio.OperadorId;
        ComercioId = envio.ComercioId;
        Codigo = codigo;
        PesoKg = datos.PesoKg;
        LargoCm = datos.LargoCm;
        AnchoCm = datos.AnchoCm;
        AltoCm = datos.AltoCm;
        MontoTarifa = datos.MontoTarifa;
    }
}
