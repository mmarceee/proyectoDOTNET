namespace Logistica.Modules.Administracion.Presentation.Features.DefinirZonas;

// Traducciones entre lo que escribe el administrador en las pantallas de CU-04 y lo que reciben los handlers.
internal static class TextosZona
{
    // De lunes a domingo, en el orden en que se muestran.
    public static readonly DayOfWeek[] DiasDeLaSemana =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    // "11000, 11100" o uno por línea: el administrador los escribe como le resulte cómodo.
    public static string[] SepararCodigosPostales(string texto)
    {
        return texto.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
    }

    public static string NombreDia(DayOfWeek dia) => dia switch
    {
        DayOfWeek.Monday => "Lun",
        DayOfWeek.Tuesday => "Mar",
        DayOfWeek.Wednesday => "Mié",
        DayOfWeek.Thursday => "Jue",
        DayOfWeek.Friday => "Vie",
        DayOfWeek.Saturday => "Sáb",
        _ => "Dom"
    };

    public static string Dias(IEnumerable<DayOfWeek> dias)
    {
        return string.Join(", ", DiasDeLaSemana.Where(dias.Contains).Select(NombreDia));
    }
}
