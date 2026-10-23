using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Services;

/// <summary>En qué moneda se muestran los reportes.</summary>
public enum VistaMoneda
{
    /// <summary>Solo lo gastado en pesos.</summary>
    UYU,
    /// <summary>Solo lo gastado en dólares.</summary>
    USD,
    /// <summary>Todo en pesos: los dólares se convierten con el tipo de cambio de cada mes.</summary>
    Pesificado,
}

public record SerieMes(DateOnly Mes, decimal Contado, decimal Cuotas, decimal Fijos, bool Proyectado)
{
    public decimal Total => Contado + Cuotas + Fijos;
}

public record TotalGrupo(string Nombre, string Color, string? Icono, decimal Total, int Cantidad);

public record Reporte(
    DateOnly Desde,
    DateOnly Hasta,
    DateOnly MesActual,
    VistaMoneda Vista,
    List<SerieMes> Meses,
    List<TotalGrupo> PorCategoria,
    List<TotalGrupo> PorTarjeta,
    /// <summary>Meses con gastos en dólares que no se pudieron convertir por falta de tipo de cambio.</summary>
    List<DateOnly> MesesSinTipoCambio)
{
    /// <summary>Meses ya transcurridos (hasta el mes en curso inclusive): los que cuentan para totales y promedios.</summary>
    public IEnumerable<SerieMes> Reales => Meses.Where(m => !m.Proyectado);
    /// <summary>Meses transcurridos desde el primero con gastos: los meses vacíos del principio no bajan el promedio.</summary>
    public IEnumerable<SerieMes> ConDatos => Reales.SkipWhile(m => m.Total == 0);
    public int MesesConDatos => ConDatos.Count();
    public decimal Total => Reales.Sum(m => m.Total);
    public decimal Promedio => MesesConDatos > 0 ? decimal.Round(Total / MesesConDatos, 2) : 0;
    public SerieMes? MesMaximo => Reales.Where(m => m.Total > 0).MaxBy(m => m.Total);
    public TotalGrupo? CategoriaPrincipal => PorCategoria.FirstOrDefault();
    public Moneda MonedaVisible => Vista == VistaMoneda.USD ? Moneda.USD : Moneda.UYU;
}

/// <summary>
/// Cotizaciones para convertir dólares a pesos. Para cada mes manda el ajuste manual del usuario; si no hay,
/// la cotización del BCU de ese mes; y si tampoco, la del último mes anterior que tenga alguna de las dos.
/// </summary>
public class TablaTiposCambio
{
    private readonly SortedDictionary<DateOnly, (decimal Valor, bool Manual)> _porMes = new();

    public TablaTiposCambio(IEnumerable<TipoCambio> manuales, IEnumerable<CotizacionBcu>? bcu = null)
    {
        foreach (var c in bcu ?? Enumerable.Empty<CotizacionBcu>()) _porMes[PlanDeCuotas.Periodo(c.Mes)] = (c.UsdEnPesos, false);
        foreach (var t in manuales) _porMes[PlanDeCuotas.Periodo(t.Mes)] = (t.UsdEnPesos, true);
    }

    public decimal? Para(DateOnly mes)
    {
        var p = PlanDeCuotas.Periodo(mes);
        decimal? ultimo = null;
        foreach (var (m, v) in _porMes)
        {
            if (m > p) break;
            ultimo = v.Valor;
        }
        return ultimo;
    }

    /// <summary>true si el valor de ese mes exacto lo cargó el usuario.</summary>
    public bool EsManual(DateOnly mes) => _porMes.TryGetValue(PlanDeCuotas.Periodo(mes), out var v) && v.Manual;

    /// <summary>Monto en la moneda de la vista, o null si no corresponde o no se puede convertir.</summary>
    public decimal? Convertir(decimal monto, Moneda moneda, DateOnly mes, VistaMoneda vista) => vista switch
    {
        VistaMoneda.UYU => moneda == Moneda.UYU ? monto : null,
        VistaMoneda.USD => moneda == Moneda.USD ? monto : null,
        _ => moneda == Moneda.UYU ? monto : Para(mes) is decimal tc ? decimal.Round(monto * tc, 2) : null,
    };
}

/// <summary>Datos de los dashboards. Todo pasa por el filtro por usuario del DbContext.</summary>
public class ReporteService(AppDbContext db, GastoService gastos, CotizacionService? cotizaciones = null)
{
    public const int MaxMeses = 36;

    public async Task<TablaTiposCambio> TiposCambioAsync() =>
        new(await db.TiposCambio.AsNoTracking().ToListAsync(), await db.CotizacionesBcu.AsNoTracking().ToListAsync());

    public async Task<Reporte> GenerarAsync(DateOnly desde, DateOnly hasta, DateOnly mesActual, VistaMoneda vista)
    {
        desde = PlanDeCuotas.Periodo(desde);
        hasta = PlanDeCuotas.Periodo(hasta);
        mesActual = PlanDeCuotas.Periodo(mesActual);
        if (hasta < desde) (desde, hasta) = (hasta, desde);
        if (desde.AddMonths(MaxMeses - 1) < hasta) desde = hasta.AddMonths(-(MaxMeses - 1));

        // Para ver todo en pesos, primero se completan las cotizaciones del BCU que falten (si la API responde).
        if (vista == VistaMoneda.Pesificado && cotizaciones != null)
            await cotizaciones.AsegurarAsync(desde, hasta < mesActual ? hasta : mesActual);
        var tc = await TiposCambioAsync();
        var cuotas = await db.Cuotas.AsNoTracking()
            .Where(c => c.Periodo >= desde && c.Periodo <= hasta)
            .Select(c => new
            {
                c.Periodo,
                c.Monto,
                c.Gasto.Moneda,
                EsFijo = c.Gasto.GastoRecurrenteId != null,
                EnCuotas = c.Gasto.CantidadCuotas > 1,
                Categoria = c.Gasto.Categoria == null ? null : new { c.Gasto.Categoria.Nombre, c.Gasto.Categoria.Color, c.Gasto.Categoria.Icono },
                Tarjeta = c.Gasto.Tarjeta == null ? null : new { c.Gasto.Tarjeta.Nombre, c.Gasto.Tarjeta.Color },
            })
            .ToListAsync();

        var sinTc = new HashSet<DateOnly>();
        var convertidas = new List<(DateOnly Mes, decimal Monto, bool EsFijo, bool EnCuotas, string Cat, string CatColor, string? CatIcono, string Tar, string TarColor)>();
        foreach (var c in cuotas)
        {
            var monto = tc.Convertir(c.Monto, c.Moneda, c.Periodo, vista);
            if (monto == null)
            {
                if (vista == VistaMoneda.Pesificado && c.Moneda == Moneda.USD) sinTc.Add(c.Periodo);
                continue;
            }
            convertidas.Add((c.Periodo, monto.Value, c.EsFijo, c.EnCuotas,
                c.Categoria?.Nombre ?? "Sin categoría", c.Categoria?.Color ?? "#94a3b8", c.Categoria?.Icono ?? "bi-question-circle",
                c.Tarjeta?.Nombre ?? "Efectivo / transferencia", c.Tarjeta?.Color ?? "#64748b"));
        }

        // Gastos fijos de meses futuros que todavía no se generaron.
        var proyeccion = hasta > mesActual
            ? await gastos.ProyeccionAsync(mesActual.AddMonths(1), (hasta.Year - mesActual.Year) * 12 + hasta.Month - mesActual.Month)
            : new List<MesProyectado>();

        var meses = new List<SerieMes>();
        for (var m = desde; m <= hasta; m = m.AddMonths(1))
        {
            var delMes = convertidas.Where(x => x.Mes == m).ToList();
            decimal fijos = delMes.Where(x => x.EsFijo).Sum(x => x.Monto);
            if (m > mesActual && proyeccion.FirstOrDefault(p => p.Mes == m) is MesProyectado p)
            {
                decimal previstos = vista switch
                {
                    VistaMoneda.UYU => p.PrevistoUYU,
                    VistaMoneda.USD => p.PrevistoUSD,
                    _ => p.PrevistoUYU + (tc.Convertir(p.PrevistoUSD, Moneda.USD, m, vista) ?? 0),
                };
                fijos += previstos;
            }
            meses.Add(new SerieMes(
                m,
                delMes.Where(x => !x.EsFijo && !x.EnCuotas).Sum(x => x.Monto),
                delMes.Where(x => !x.EsFijo && x.EnCuotas).Sum(x => x.Monto),
                fijos,
                Proyectado: m > mesActual));
        }

        // Categorías y tarjetas: solo meses transcurridos (lo futuro es una estimación).
        var reales = convertidas.Where(x => x.Mes <= mesActual).ToList();
        var porCategoria = reales.GroupBy(x => x.Cat)
            .Select(g => new TotalGrupo(g.Key, g.First().CatColor, g.First().CatIcono, g.Sum(x => x.Monto), g.Count()))
            .OrderByDescending(t => t.Total).ToList();
        var porTarjeta = reales.GroupBy(x => x.Tar)
            .Select(g => new TotalGrupo(g.Key, g.First().TarColor, null, g.Sum(x => x.Monto), g.Count()))
            .OrderByDescending(t => t.Total).ToList();

        return new Reporte(desde, hasta, mesActual, vista, meses, porCategoria, porTarjeta, sinTc.OrderBy(x => x).ToList());
    }

    /// <summary>Filas para exportar a CSV: una por cada cuota (o gasto al contado) entre los dos meses.</summary>
    public async Task<List<Cuota>> MovimientosAsync(DateOnly desde, DateOnly hasta)
    {
        desde = PlanDeCuotas.Periodo(desde);
        hasta = PlanDeCuotas.Periodo(hasta);
        return await db.Cuotas.AsNoTracking()
            .Include(c => c.Gasto).ThenInclude(g => g.Categoria)
            .Include(c => c.Gasto).ThenInclude(g => g.Tarjeta)
            .Where(c => c.Periodo >= desde && c.Periodo <= hasta)
            .OrderBy(c => c.Periodo).ThenBy(c => c.Gasto.Fecha).ThenBy(c => c.Id)
            .ToListAsync();
    }
}
