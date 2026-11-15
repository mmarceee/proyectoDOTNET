using Logistica.Modules.Seguimiento.Domain.Reprogramaciones;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Seguimiento;

// CU-61: la verificación que se pide antes de reprogramar desde el seguimiento público.
public class VerificacionReprogramacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 11, 18);
    private static readonly Guid Franja = Guid.NewGuid();
    private static readonly byte[] Secreto = [1, 2, 3, 4];
    private static readonly byte[] OtroSecreto = [9, 9, 9, 9];

    private static VerificacionReprogramacion Nueva() => VerificacionReprogramacion.Crear(
        Guid.NewGuid(), Guid.NewGuid(), Fecha, Franja, MedioVerificacion.Correo, Secreto, Ahora);

    [Fact]
    public void Con_el_secreto_correcto_es_valida_y_vence_a_los_diez_minutos()
    {
        var verificacion = Nueva();

        Assert.Equal(Ahora.AddMinutes(10), verificacion.VenceEn);
        Assert.Equal(ResultadoVerificacion.Valida, verificacion.Comprobar(Fecha, Franja, Secreto, Ahora.AddMinutes(9)));
        Assert.Equal(0, verificacion.Intentos);
    }

    [Fact]
    public void Despues_de_los_diez_minutos_esta_vencida()
    {
        var verificacion = Nueva();

        Assert.Equal(ResultadoVerificacion.Vencida, verificacion.Comprobar(Fecha, Franja, Secreto, Ahora.AddMinutes(10)));
    }

    [Fact]
    public void Tres_secretos_incorrectos_la_agotan_aunque_despues_llegue_el_correcto()
    {
        var verificacion = Nueva();

        Assert.Equal(ResultadoVerificacion.Incorrecta, verificacion.Comprobar(Fecha, Franja, OtroSecreto, Ahora));
        Assert.Equal(ResultadoVerificacion.Incorrecta, verificacion.Comprobar(Fecha, Franja, OtroSecreto, Ahora));
        Assert.Equal(ResultadoVerificacion.Agotada, verificacion.Comprobar(Fecha, Franja, OtroSecreto, Ahora));

        Assert.Equal(ResultadoVerificacion.Agotada, verificacion.Comprobar(Fecha, Franja, Secreto, Ahora));
        Assert.Equal(0, verificacion.IntentosRestantes);
    }

    // El código vale para la fecha y franja que se eligieron: no sirve para otra, y probarlo no gasta un intento.
    [Fact]
    public void Para_otra_fecha_o_franja_no_vale_y_no_gasta_intentos()
    {
        var verificacion = Nueva();

        Assert.Equal(ResultadoVerificacion.OtraOpcion, verificacion.Comprobar(Fecha.AddDays(1), Franja, Secreto, Ahora));
        Assert.Equal(ResultadoVerificacion.OtraOpcion, verificacion.Comprobar(Fecha, Guid.NewGuid(), Secreto, Ahora));
        Assert.Equal(VerificacionReprogramacion.MaxIntentos, verificacion.IntentosRestantes);
    }

    [Fact]
    public void Se_usa_una_sola_vez()
    {
        var verificacion = Nueva();
        verificacion.MarcarUsada(Ahora);

        Assert.Equal(ResultadoVerificacion.Usada, verificacion.Comprobar(Fecha, Franja, Secreto, Ahora));
        Assert.Throws<DomainException>(() => verificacion.MarcarUsada(Ahora));
    }

    [Fact]
    public void Anulada_por_un_pedido_nuevo_ya_no_vale()
    {
        var verificacion = Nueva();
        verificacion.Anular();

        Assert.Equal(ResultadoVerificacion.Vencida, verificacion.Comprobar(Fecha, Franja, Secreto, Ahora));
    }

    [Fact]
    public void Sin_secreto_no_se_crea()
    {
        Assert.Throws<DomainException>(() => VerificacionReprogramacion.Crear(
            Guid.NewGuid(), Guid.NewGuid(), Fecha, Franja, MedioVerificacion.Telefono, [], Ahora));
    }
}
