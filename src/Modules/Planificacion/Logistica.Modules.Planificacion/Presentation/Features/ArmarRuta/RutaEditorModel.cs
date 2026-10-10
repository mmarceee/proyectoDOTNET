using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.Modules.Planificacion.Application.Features.ArmarRuta;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRutas;
using Logistica.Modules.Planificacion.Application.Features.ConsultarEnviosDisponibles;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Planificacion.Presentation.Features.ArmarRuta;

[TypeFilter(typeof(AccesoPlanificacion))]
internal abstract class RutaEditorModel(ArmarRutaHandler service, ConsultarRutasHandler consultas, ConsultarEnviosDisponiblesHandler disponibles, ConsultarRecursosPlanificacionHandler recursos, IServiceScopeFactory scopes, AccesoPlanificacion acceso) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public DateOnly Fecha { get; set; }
    [BindProperty] public Guid? RepartidorId { get; set; }
    [BindProperty] public Guid? VehiculoId { get; set; }
    [BindProperty] public long RevisionEsperada { get; set; }
    [BindProperty] public List<Guid> Seleccion { get; set; } = [];
    [BindProperty] public Guid? ZonaId { get; set; }
    [BindProperty] public Guid? FranjaId { get; set; }
    [BindProperty] public string? Texto { get; set; }
    [BindProperty] public int Pagina { get; set; } = 1;
    [BindProperty] public bool EditandoDatos { get; set; }
    [BindProperty] public bool ModalEnvios { get; set; }
    [TempData] public string? MensajeConfirmacion { get; set; }
    public string? SeccionResultado { get; private set; }
    public int TotalCandidatos { get; private set; }
    public DateOnly Hoy { get; private set; }
    public IReadOnlyDictionary<Guid, string> NumerosEnvio { get; private set; } = new Dictionary<Guid, string>();
    public RecursosPlanificacion Recursos { get; private set; } = new([], [], [], []);
    public IReadOnlyList<EnvioDisponibleResponse> Candidatos { get; private set; } = [];
    public RutaResponse? Ruta { get; private set; }
    public EvaluacionRutaResponse? Evaluacion { get; private set; }
    public IReadOnlyList<ValidacionRutaResponse> Historial { get; private set; } = [];
    public bool Desarrollo => acceso.Desarrollo;
    public bool Editable => Ruta is null || Ruta.Estado == "Planificada";
    public bool CambioDatos => Ruta is not null && (Ruta.Fecha != Fecha || Ruta.RepartidorId != RepartidorId || Ruta.VehiculoId != VehiculoId);
    public bool SeleccionValidada => Id is not null && Evaluacion?.Valida == true && Seleccion.Count > 0 && !CambioDatos && !EditandoDatos;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        try
        {
            Fecha = await recursos.HoyAsync(ct);
            if (Id is Guid id)
            {
                Ruta = await consultas.DetalleAsync(id, ct); Fecha = Ruta.Fecha; RepartidorId = Ruta.RepartidorId;
                VehiculoId = Ruta.VehiculoId; RevisionEsperada = Ruta.Revision;
            }
            await CargarAsync(consultas, disponibles, recursos, ct);
            return Page();
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
    }
    public async Task<IActionResult> OnPostFiltrarAsync(CancellationToken ct)
    { await CargarAsync(consultas, disponibles, recursos, ct); return ResultadoEnvios(); }
    public async Task<IActionResult> OnPostLimpiarFiltrosAsync(CancellationToken ct)
    {
        ZonaId = null; FranjaId = null; Texto = null; Pagina = 1;
        foreach (var campo in new[] { nameof(ZonaId), nameof(FranjaId), nameof(Texto), nameof(Pagina) })
            ModelState.Remove(campo);
        await CargarAsync(consultas, disponibles, recursos, ct);
        return ResultadoEnvios();
    }
    public async Task<IActionResult> OnPostEditarDatosAsync(CancellationToken ct)
    {
        CambiarModalEnvios(false);
        EditandoDatos = true;
        ModelState.Remove(nameof(EditandoDatos));
        SeccionResultado = "datos-ruta";
        await CargarAsync(consultas, disponibles, recursos, ct);
        return Page();
    }
    public async Task<IActionResult> OnPostCancelarDatosAsync(CancellationToken ct)
    {
        CambiarModalEnvios(false);
        if (Id is not Guid id) return NotFound();
        try
        {
            var actual = await consultas.DetalleAsync(id, ct);
            Fecha = actual.Fecha; RepartidorId = actual.RepartidorId; VehiculoId = actual.VehiculoId;
            EditandoDatos = false;
            foreach (var campo in new[] { nameof(Fecha), nameof(RepartidorId), nameof(VehiculoId), nameof(EditandoDatos) })
                ModelState.Remove(campo);
            SeccionResultado = "datos-ruta";
            await CargarAsync(consultas, disponibles, recursos, ct);
            return Page();
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
    }
    public async Task<IActionResult> OnPostValidarAsync(CancellationToken ct)
    {
        CambiarModalEnvios(Id is not null && !EditandoDatos);
        SeccionResultado = "validacion-ruta";
        if (Id is not null && Seleccion.Count == 0)
            ModelState.AddModelError(string.Empty, "Seleccioná al menos un envío para validar el agregado.");
        if (!ValidarRecursos()) { await CargarFrescoAsync(ct); return ResultadoEnvios(); }
        try { Evaluacion = await service.PrevalidarAsync(new(Fecha, RepartidorId.GetValueOrDefault(), VehiculoId.GetValueOrDefault(), Seleccion, Id, Id is null ? null : RevisionEsperada), ct); }
        catch (Exception e) when (Esperada(e)) { MostrarError(e); }
        await CargarFrescoAsync(ct);
        if (SeleccionValidada)
        {
            CambiarModalEnvios(false);
            SeccionResultado = "confirmacion-envios";
        }
        return ResultadoEnvios();
    }
    public Task<IActionResult> OnPostConfirmarAsync(CancellationToken ct) => GuardarAsync(false, ct);
    public Task<IActionResult> OnPostModificarAsync(CancellationToken ct) => GuardarAsync(true, ct);
    private async Task<IActionResult> GuardarAsync(bool modificar, CancellationToken ct)
    {
        CambiarModalEnvios(Id is not null && !modificar);
        if (modificar && Id is not null)
        {
            EditandoDatos = true;
            ModelState.Remove(nameof(EditandoDatos));
        }
        SeccionResultado = "errores-ruta";
        if (!ValidarRecursos()) { await CargarFrescoAsync(ct); return Page(); }
        try
        {
            RutaResponse ruta;
            if (Id is not Guid id) ruta = await service.CrearAsync(new(Fecha, RepartidorId.GetValueOrDefault(), VehiculoId.GetValueOrDefault(), Seleccion), ct);
            else if (modificar)
            {
                var actual = await consultas.DetalleAsync(id, ct);
                if (actual.Fecha == Fecha && actual.RepartidorId == RepartidorId && actual.VehiculoId == VehiculoId)
                    throw new DomainException("No hay cambios para guardar. Cambiá la fecha, el repartidor o el vehículo primero.");
                ruta = await service.ModificarAsync(id, new(RevisionEsperada, Fecha, RepartidorId.GetValueOrDefault(), VehiculoId.GetValueOrDefault()), ct);
            }
            else
            {
                var actual = await consultas.DetalleAsync(id, ct);
                if (actual.Fecha != Fecha || actual.RepartidorId != RepartidorId || actual.VehiculoId != VehiculoId)
                    throw new DomainException("Confirmá primero el cambio de fecha o recursos y después agregá los envíos.");
                ruta = await service.AgregarAsync(id, new(RevisionEsperada, Seleccion), ct);
            }
            MensajeConfirmacion = Id is null ? "Ruta creada y envíos asignados correctamente."
                : modificar ? "Fecha y recursos guardados correctamente."
                : $"Se agregaron {Seleccion.Count} envíos a la ruta. Podés verlos en Paradas actuales.";
            return Redirect($"/backoffice/rutas/{ruta.Id}#resultado-ruta");
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
        catch (Exception e) when (Esperada(e)) { MostrarError(e); }
        await CargarFrescoAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostAbrirEnviosAsync(CancellationToken ct)
    {
        if (Id is null) return NotFound();
        try
        {
            Seleccion.Clear(); ModelState.Remove(nameof(Seleccion));
            await CargarAsync(consultas, disponibles, recursos, ct);
            if (!Editable || EditandoDatos) return BadRequest();
            CambiarModalEnvios(true);
            return Page();
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
    }
    public async Task<IActionResult> OnPostCerrarEnviosAsync(CancellationToken ct)
    {
        Seleccion.Clear(); ModelState.Remove(nameof(Seleccion));
        CambiarModalEnvios(false);
        SeccionResultado = "paradas-actuales";
        await CargarAsync(consultas, disponibles, recursos, ct);
        return Page();
    }
    private IActionResult ResultadoEnvios()
        => Id is not null && Request.Headers["X-Requested-With"] == "XMLHttpRequest"
            ? Partial("~/Presentation/Features/ArmarRuta/ContenidoModalEnvios.cshtml", this)
            : Page();
    private void CambiarModalEnvios(bool abrir)
    {
        ModalEnvios = abrir;
        ModelState.Remove(nameof(ModalEnvios));
    }
    private bool ValidarRecursos()
    {
        if (RepartidorId is null || RepartidorId == Guid.Empty)
            ModelState.AddModelError(nameof(RepartidorId), "Seleccioná un repartidor.");
        if (VehiculoId is null || VehiculoId == Guid.Empty)
            ModelState.AddModelError(nameof(VehiculoId), "Seleccioná un vehículo.");
        if (!ModelState.IsValid) SeccionResultado = "errores-ruta";
        return ModelState.IsValid;
    }
    private static bool Esperada(Exception e) => e is DomainException or ConflictoRutaException or RutaNoEncontradaException;
    private void MostrarError(Exception e)
    {
        SeccionResultado = "errores-ruta";
        ModelState.AddModelError(string.Empty, e.Message);
        if (e is ConflictoRutaException conflicto)
        {
            Seleccion.RemoveAll(id => conflicto.EnvioIds.Contains(id));
            ModelState.Remove(nameof(Seleccion));
        }
    }
    private async Task CargarFrescoAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await CargarAsync(scope.ServiceProvider.GetRequiredService<ConsultarRutasHandler>(),
            scope.ServiceProvider.GetRequiredService<ConsultarEnviosDisponiblesHandler>(),
            scope.ServiceProvider.GetRequiredService<ConsultarRecursosPlanificacionHandler>(), ct);
    }
    private async Task CargarAsync(ConsultarRutasHandler lectorRutas, ConsultarEnviosDisponiblesHandler lectorEnvios, ConsultarRecursosPlanificacionHandler lectorRecursos, CancellationToken ct)
    {
        Hoy = await lectorRecursos.HoyAsync(ct);
        Recursos = await lectorRecursos.RecursosAsync(Fecha, ct);
        Pagina = Math.Max(1, Pagina);
        var visibles = await lectorEnvios.DisponiblesAsync(Fecha, ZonaId, FranjaId, Texto, Pagina, 100, ct);
        TotalCandidatos = visibles.Total;
        var seleccionados = new List<EnvioDisponibleResponse>();
        for (var pagina = 1; pagina <= (Seleccion.Count + 99) / 100; pagina++)
            seleccionados.AddRange((await lectorEnvios.DisponiblesAsync(Fecha, null, null, null, pagina, 100, ct, Seleccion)).Items);
        Candidatos = visibles.Items.Concat(seleccionados).DistinctBy(e => e.Id).ToList();
        if (Id is Guid id)
        {
            Ruta = await lectorRutas.DetalleAsync(id, ct);
            Historial = await lectorRutas.ValidacionesAsync(id, 1, ct);
            NumerosEnvio = await lectorRutas.NumerosEnvioAsync(Ruta.Paradas.Select(p => p.EnvioId)
                .Concat(Historial.SelectMany(v => v.Bultos).Select(b => b.EnvioId)).Distinct().ToArray(), ct);
            // Una nueva confirmación debe usar la revisión recién mostrada, sin reenviar automáticamente.
            RevisionEsperada = Ruta.Revision; ModelState.Remove(nameof(RevisionEsperada));
        }
    }
}
