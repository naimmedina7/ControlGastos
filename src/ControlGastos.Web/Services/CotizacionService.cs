using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Services;

/// <summary>
/// Cotización mensual del dólar del Banco Central del Uruguay, tomada de la API gratuita de Frankfurter
/// (sin clave). Se guarda en la base: cada mes cerrado se consulta una sola vez y el mes en curso,
/// como mucho una vez por día. Si la API no responde, se sigue con lo que ya está guardado.
/// </summary>
public class CotizacionService(
    AppDbContext db, HttpClient http, Reloj reloj, IConfiguration config, ILogger<CotizacionService> logger)
{
    public const string Fuente = "Banco Central del Uruguay, vía Frankfurter";
    private static readonly DateOnly PrimerMesDisponible = new(2000, 1, 1);

    private bool Habilitado => config.GetValue("Cotizaciones:Habilitado", true);

    private record TasaApi([property: JsonPropertyName("date")] string Fecha, [property: JsonPropertyName("rate")] decimal Valor);

    /// <summary>
    /// Se asegura de tener la cotización de cada mes entre <paramref name="desde"/> y <paramref name="hasta"/>
    /// (sin pasar del mes en curso). Devuelve cuántos meses guardó o actualizó.
    /// </summary>
    public async Task<int> AsegurarAsync(DateOnly desde, DateOnly hasta, bool forzar = false)
    {
        if (!Habilitado) return 0;
        var mesActual = reloj.MesActual;
        desde = PlanDeCuotas.Periodo(desde);
        hasta = PlanDeCuotas.Periodo(hasta);
        if (hasta > mesActual) hasta = mesActual;
        if (desde < PrimerMesDisponible) desde = PrimerMesDisponible;
        if (desde > hasta) return 0;

        var guardadas = await db.CotizacionesBcu.Where(c => c.Mes >= desde && c.Mes <= hasta).ToDictionaryAsync(c => c.Mes);
        var hoyUtc = DateTime.UtcNow.Date;
        var faltan = new List<DateOnly>();
        for (var m = desde; m <= hasta; m = m.AddMonths(1))
        {
            if (!guardadas.TryGetValue(m, out var c)) faltan.Add(m);
            else if (!c.Definitiva && (m < mesActual || forzar || c.ActualizadoEn.Date < hoyUtc)) faltan.Add(m);
        }
        if (faltan.Count == 0) return 0;

        List<TasaApi>? tasas;
        try
        {
            var inicio = faltan.Min();
            var fin = faltan.Max() >= mesActual ? reloj.Hoy : faltan.Max().AddMonths(1).AddDays(-1);
            string url = "v2/rates?base=USD&quotes=UYU&providers=bcu&group=month" +
                         $"&from={inicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
                         $"&to={fin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            tasas = await http.GetFromJsonAsync<List<TasaApi>>(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "No se pudo consultar la cotización del BCU; se usa lo guardado.");
            return 0;
        }

        int cambios = 0;
        foreach (var t in tasas ?? new())
        {
            if (!DateOnly.TryParseExact(t.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)) continue;
            var mes = PlanDeCuotas.Periodo(fecha);
            // Valores fuera de rango razonable se descartan (protección ante una respuesta corrupta).
            if (!faltan.Contains(mes) || t.Valor is <= 0 or > 100_000) continue;

            if (!guardadas.TryGetValue(mes, out var c))
            {
                c = new CotizacionBcu { Mes = mes };
                db.CotizacionesBcu.Add(c);
                guardadas[mes] = c;
            }
            c.UsdEnPesos = decimal.Round(t.Valor, 4);
            c.Definitiva = mes < mesActual;
            c.ActualizadoEn = DateTime.UtcNow;
            cambios++;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Otra request guardó el mismo mes al mismo tiempo: alcanza con lo que guardó ella.
            logger.LogInformation(ex, "Cotizaciones guardadas en paralelo; se descarta este intento.");
            foreach (var e in db.ChangeTracker.Entries<CotizacionBcu>().ToList()) e.State = EntityState.Detached;
            return 0;
        }
        return cambios;
    }

    public Task<List<CotizacionBcu>> ListarAsync(DateOnly desde, DateOnly hasta) =>
        db.CotizacionesBcu.AsNoTracking().Where(c => c.Mes >= desde && c.Mes <= hasta).OrderByDescending(c => c.Mes).ToListAsync();
}
