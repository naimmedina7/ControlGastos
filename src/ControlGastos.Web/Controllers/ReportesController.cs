using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ControlGastos.Web.Controllers;

[Route("reportes")]
[GenerarRecurrentes]
public class ReportesController(ReporteService reportes, Reloj reloj) : Controller
{
    /// <summary>
    /// Dashboards. <paramref name="rango"/>: "6" o "12" (últimos meses + 3 de proyección), "anio", "anio-anterior";
    /// o <paramref name="desde"/>/<paramref name="hasta"/> (yyyy-MM) para un período a medida.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? rango, string? desde, string? hasta, VistaMoneda vista = VistaMoneda.UYU)
    {
        var mes = reloj.MesActual;
        var (d, h, r) = ResolverRango(rango, desde, hasta, mes);
        if (!Enum.IsDefined(vista)) vista = VistaMoneda.UYU;
        var reporte = await reportes.GenerarAsync(d, h, mes, vista);
        return View(new ReporteVm { Reporte = reporte, Rango = r, MesesFuturos = reporte.Meses.Count(m => m.Proyectado) });
    }

    /// <summary>Descarga los movimientos del período en CSV (para Excel o Google Sheets).</summary>
    [HttpGet("exportar")]
    public async Task<IActionResult> Exportar(string? desde, string? hasta)
    {
        var mes = reloj.MesActual;
        var d = Fmt.LeerMes(desde) ?? mes;
        var h = Fmt.LeerMes(hasta) ?? d;
        if (h < d) (d, h) = (h, d);
        if (d.AddMonths(ReporteService.MaxMeses - 1) < h) d = h.AddMonths(-(ReporteService.MaxMeses - 1));

        var filas = await reportes.MovimientosAsync(d, h);
        string nombre = d == h ? $"gastos-{Fmt.MesParam(d)}.csv" : $"gastos-{Fmt.MesParam(d)}-a-{Fmt.MesParam(h)}.csv";
        return File(ExportadorCsv.Generar(filas), "text/csv; charset=utf-8", nombre);
    }

    internal static (DateOnly Desde, DateOnly Hasta, string Rango) ResolverRango(string? rango, string? desde, string? hasta, DateOnly mes)
    {
        if (Fmt.LeerMes(desde) is DateOnly d && Fmt.LeerMes(hasta) is DateOnly h)
            return (d, h, "personalizado");
        return rango switch
        {
            "6" => (mes.AddMonths(-5), mes.AddMonths(3), "6"),
            "anio" => (new DateOnly(mes.Year, 1, 1), new DateOnly(mes.Year, 12, 1), "anio"),
            "anio-anterior" => (new DateOnly(mes.Year - 1, 1, 1), new DateOnly(mes.Year - 1, 12, 1), "anio-anterior"),
            _ => (mes.AddMonths(-11), mes.AddMonths(3), "12"),
        };
    }
}
