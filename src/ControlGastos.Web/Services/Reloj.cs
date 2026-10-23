namespace ControlGastos.Web.Services;

/// <summary>
/// Fecha "de hoy" en la zona horaria de la app (Uruguay por defecto). El servidor suele correr en UTC,
/// y sin esto un gasto cargado a las 22 h quedaría con la fecha del día siguiente.
/// </summary>
public class Reloj(IConfiguration config, TimeProvider tiempo)
{
    private readonly TimeZoneInfo _zona = BuscarZona(config["App:ZonaHoraria"] ?? "America/Montevideo");

    public DateTime Ahora => TimeZoneInfo.ConvertTime(tiempo.GetUtcNow(), _zona).DateTime;
    public DateOnly Hoy => DateOnly.FromDateTime(Ahora);
    public DateOnly MesActual => new(Hoy.Year, Hoy.Month, 1);

    /// <summary>Convierte una fecha UTC guardada en la base a la hora local de la app.</summary>
    public DateTime ALocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zona);

    private static TimeZoneInfo BuscarZona(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }
}
