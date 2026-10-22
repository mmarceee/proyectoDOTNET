using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;
using Logistica.Modules.Seguimiento.Application.HechosEnvio;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Domain.Notificaciones;
using Logistica.Modules.Seguimiento.Infrastructure.Correo;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.SharedKernel;
using MimeKit;

namespace Logistica.UnitTests.Seguimiento;

public class NotificacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static EnvioSeguido Envio(string? email) =>
        EnvioSeguido.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ENV-000321", "Ana Pérez", email);

    private static HechoEnvio Hecho(string estado, bool intentoFallido = false, DateOnly? fecha = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "ENV-1", estado, intentoFallido, Ahora, false, NuevaFecha: fecha);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("no-es-un-correo")]
    public void Sin_un_correo_utilizable_la_notificacion_es_no_enviable(string? email)
    {
        var notificacion = Notificacion.Crear(Envio(email), Guid.NewGuid(), "Admitido", Ahora);

        Assert.Equal(EstadoNotificacion.NoEnviable, notificacion.Estado);
        Assert.False(notificacion.PorEnviar);
        Assert.Throws<DomainException>(() => notificacion.RegistrarEnvio(Ahora));
    }

    [Fact]
    public void Con_correo_nace_pendiente_y_copia_el_destino()
    {
        var envio = Envio(" ana@example.com ");
        var evento = Guid.NewGuid();
        var notificacion = Notificacion.Crear(envio, evento, "EnTransito", Ahora);

        Assert.Equal(EstadoNotificacion.Pendiente, notificacion.Estado);
        Assert.Equal("ana@example.com", notificacion.Destino);
        Assert.Equal((evento, envio.Id), (notificacion.EventoId, notificacion.EnvioId));
        Assert.Equal((CanalNotificacion.Email, TipoNotificacion.CambioDeEstado), (notificacion.Canal, notificacion.Tipo));
        Assert.Equal((envio.OperadorId, envio.ComercioId), (notificacion.OperadorId, notificacion.ComercioId));
    }

    [Fact]
    public void Los_fallos_se_cuentan_el_ultimo_la_deja_fallida_y_un_reintento_la_envia()
    {
        var notificacion = Notificacion.Crear(Envio("ana@example.com"), Guid.NewGuid(), "Entregado", Ahora);
        notificacion.RegistrarFallo(new string('x', 900), ultimo: false);
        Assert.Equal((EstadoNotificacion.Pendiente, 1), (notificacion.Estado, notificacion.Intentos));
        Assert.Equal(Notificacion.LargoError, notificacion.UltimoError!.Length);

        notificacion.RegistrarFallo("Sin conexión.", ultimo: true);
        Assert.Equal(EstadoNotificacion.Fallida, notificacion.Estado);

        // CU-09 vuelve a publicar el mensaje: la notificación fallida se puede enviar.
        notificacion.RegistrarEnvio(Ahora);
        Assert.Equal((EstadoNotificacion.Enviada, 3), (notificacion.Estado, notificacion.Intentos));
        Assert.Equal(Ahora, notificacion.EnviadaEn);
        Assert.Null(notificacion.UltimoError);
        Assert.Throws<DomainException>(() => notificacion.RegistrarEnvio(Ahora));
    }

    [Fact]
    public void Una_identidad_anterior_no_pisa_a_una_mas_nueva()
    {
        var identidad = IdentidadOperador.Crear(Guid.NewGuid(), "Operador Norte", "#112233", "#445566", null, null, Ahora);

        Assert.False(identidad.Actualizar("Viejo", "#000000", "#000000", null, null, Ahora.AddMinutes(-1)));
        Assert.True(identidad.Actualizar("Operador Sur", "#AABBCC", "#DDEEFF", " contacto@sur.uy ", "", Ahora.AddMinutes(1)));
        Assert.Equal(("Operador Sur", "#AABBCC"), (identidad.Nombre, identidad.ColorPrimario));
        Assert.Equal("contacto@sur.uy", identidad.EmailContacto);
        Assert.Null(identidad.TelefonoContacto);
        Assert.Equal(identidad.OperadorId, identidad.Id);
    }

    [Theory]
    [InlineData("Admitido", false, "Recibimos tu envío ENV-9")]
    [InlineData("EnTransito", false, "Tu envío ENV-9 está en camino")]
    [InlineData("Entregado", false, "Entregamos tu envío ENV-9")]
    [InlineData("Reprogramado", true, "No pudimos entregar tu envío ENV-9")]
    [InlineData("EnDevolucion", true, "No pudimos entregar tu envío ENV-9")]
    public void Cada_hecho_relevante_tiene_su_texto(string estado, bool intentoFallido, string asunto)
    {
        var texto = TextosNotificacion.Para(Hecho(estado, intentoFallido), "ENV-9", "Operador Norte");
        Assert.Equal(asunto, texto.Asunto);
        Assert.False(string.IsNullOrWhiteSpace(texto.Texto));
    }

    [Fact]
    public void La_reprogramacion_dice_la_nueva_fecha_y_un_estado_no_relevante_no_tiene_texto()
    {
        var texto = TextosNotificacion.Para(Hecho("Reprogramado", fecha: new DateOnly(2026, 10, 27)), "ENV-9", "Operador Norte");
        Assert.Contains("martes 27 de octubre", texto.Texto);
        Assert.Throws<InvalidOperationException>(() => TextosNotificacion.Para(Hecho("AsignadoARuta"), "ENV-9", "Operador"));
    }

    [Fact]
    public void El_alta_trae_el_destinatario_y_la_reprogramacion_su_fecha()
    {
        var alta = LectorHechosEnvio.Leer(Mensaje(EnvioAdmitido.Tipo,
            new EnvioAdmitido(Guid.NewGuid(), "ENV-9", Ahora, "Ana Pérez", "ana@example.com")));
        Assert.Equal("Ana Pérez", alta.NombreDestinatario);
        Assert.Equal("ana@example.com", alta.EmailDestinatario);
        Assert.True(alta.EsAlta);

        var reprogramado = LectorHechosEnvio.Leer(Mensaje(EnvioReprogramado.Tipo,
            new EnvioReprogramado(Guid.NewGuid(), "ENV-9", new DateOnly(2026, 10, 27), Guid.NewGuid(), "Backoffice")));
        Assert.Equal(new DateOnly(2026, 10, 27), reprogramado.NuevaFecha);
    }

    [Fact]
    public void La_plantilla_codifica_los_datos_y_usa_la_marca_del_operador()
    {
        var mensaje = new MensajeCorreo(Guid.NewGuid(), "ana@example.com", "<script>Ana</script>", "Asunto",
            "Título", "Texto con <b>etiquetas</b>", "https://logistica.example/seguimiento/abc?x=1&y=2",
            new MarcaCorreo("Operador \"Norte\"", "red;background:url(x)", "contacto@norte.uy", "099 123 456"));

        var correo = PlantillaCorreo.Armar(mensaje, MailboxAddress.Parse("avisos@logistica.local"));
        var html = correo.HtmlBody;

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;b&gt;etiquetas&lt;/b&gt;", html);
        Assert.Contains("seguimiento/abc?x=1&amp;y=2", html);
        // Un color que no es #RRGGBB no entra al estilo.
        Assert.DoesNotContain("url(x)", html);
        Assert.Contains("#1F4E79", html);
        Assert.Contains("https://logistica.example/seguimiento/abc?x=1&y=2", correo.TextBody);
        Assert.Equal("Operador \"Norte\"", ((MailboxAddress)correo.From.Single()).Name);
        Assert.Equal("avisos@logistica.local", ((MailboxAddress)correo.From.Single()).Address);
        Assert.Equal("contacto@norte.uy", ((MailboxAddress)correo.ReplyTo.Single()).Address);
        Assert.Equal($"{mensaje.Id:N}@logistica", correo.MessageId);
    }

    [Fact]
    public void La_configuracion_se_valida_al_usarla()
    {
        Assert.Equal("https://logistica.example/seguimiento/t", new DireccionPublica("https://logistica.example/").Absoluta("/seguimiento/t"));
        Assert.Throws<InvalidOperationException>(() => new DireccionPublica(null).Absoluta("/x"));
        Assert.Throws<InvalidOperationException>(() => new DireccionPublica("ftp://logistica.example").Absoluta("/x"));

        var conCredenciales = new ServidorSmtp("smtp://usuario%40brevo:clave%2Fsecreta@smtp-relay.example:587", "avisos@logistica.uy");
        Assert.Equal(("usuario@brevo", "clave/secreta"), conCredenciales.Credenciales!.Value);
        Assert.Null(new ServidorSmtp("smtp://mailpit:1025", "avisos@logistica.local").Credenciales);
        Assert.Throws<InvalidOperationException>(() => new ServidorSmtp("", "avisos@logistica.local").Uri);
        Assert.Throws<InvalidOperationException>(() => new ServidorSmtp("smtp://mailpit:1025", "").Remitente);
    }

    private static MensajeIntegracion Mensaje<T>(string tipo, T contenido) =>
        new(Guid.NewGuid(), tipo, Guid.NewGuid(), null, Ahora, null, JsonSerializer.Serialize(contenido));
}
