using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Controllers;

/// <summary>
/// Cotización del dólar por mes. Se completa sola con el promedio mensual del BCU; el usuario puede cargar
/// un ajuste manual para un mes (por ejemplo, la cotización a la que le cobró la tarjeta), que tiene prioridad.
/// </summary>
[Route("tipo-cambio")]
public class TipoCambioController(AppDbContext db, Reloj reloj, CotizacionService cotizaciones) : Controller
{
    private const int MesesVisibles = 12;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        await cotizaciones.AsegurarAsync(reloj.MesActual.AddMonths(-(MesesVisibles - 1)), reloj.MesActual);
        return View(await ArmarAsync(new TipoCambioFormVm { Mes = Fmt.MesParam(reloj.MesActual) }));
    }

    /// <summary>Vuelve a consultar al BCU el mes en curso (y lo que falte).</summary>
    [HttpPost("actualizar")]
    public async Task<IActionResult> Actualizar()
    {
        int n = await cotizaciones.AsegurarAsync(reloj.MesActual.AddMonths(-(MesesVisibles - 1)), reloj.MesActual, forzar: true);
        if (n > 0) TempData["Ok"] = $"Cotizaciones actualizadas desde el BCU ({n} mes(es)).";
        else TempData["Error"] = "No se pudo actualizar ahora (el servicio no respondió o no hay datos nuevos). Se usan las cotizaciones guardadas.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("")]
    public async Task<IActionResult> Guardar([Bind(Prefix = "Form")] TipoCambioFormVm form)
    {
        var mes = Fmt.LeerMes(form.Mes);
        if (mes == null) ModelState.AddModelError("Form.Mes", "Mes inválido.");
        if (!ModelState.IsValid) return View("Index", await ArmarAsync(form));

        var existente = await db.TiposCambio.FirstOrDefaultAsync(t => t.Mes == mes);
        if (existente == null)
            db.TiposCambio.Add(new TipoCambio { Mes = mes!.Value, UsdEnPesos = decimal.Round(form.UsdEnPesos, 4) });
        else
        {
            existente.UsdEnPesos = decimal.Round(form.UsdEnPesos, 4);
            existente.ActualizadoEn = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        TempData["Ok"] = $"Ajuste manual de {Fmt.MesAnio(mes!.Value)} guardado. Ese mes usa tu valor en lugar del BCU.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/eliminar")]
    public async Task<IActionResult> Eliminar(int id)
    {
        var t = await db.TiposCambio.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        db.TiposCambio.Remove(t);
        await db.SaveChangesAsync();
        TempData["Ok"] = $"Ajuste de {Fmt.MesAnio(t.Mes)} eliminado: ese mes vuelve a usar la cotización del BCU.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<TiposCambioVm> ArmarAsync(TipoCambioFormVm form)
    {
        var mesActual = reloj.MesActual;
        var desde = mesActual.AddMonths(-(MesesVisibles - 1));
        var manuales = await db.TiposCambio.AsNoTracking().OrderByDescending(t => t.Mes).ToListAsync();
        var bcu = await db.CotizacionesBcu.AsNoTracking().ToListAsync();
        var tabla = new TablaTiposCambio(manuales, bcu);
        var bcuPorMes = bcu.ToDictionary(c => c.Mes);
        var manualPorMes = manuales.ToDictionary(t => t.Mes);

        var filas = new List<FilaCotizacion>();
        for (var m = mesActual; m >= desde; m = m.AddMonths(-1))
            filas.Add(new FilaCotizacion(m, bcuPorMes.GetValueOrDefault(m), manualPorMes.GetValueOrDefault(m), tabla.Para(m), m == mesActual));

        var mesesUsd = await db.Cuotas.Where(c => c.Gasto.Moneda == Moneda.USD && c.Periodo <= mesActual)
            .Select(c => c.Periodo).Distinct().ToListAsync();

        return new TiposCambioVm
        {
            Meses = filas,
            // Ajustes manuales fuera de los 12 meses visibles (para poder verlos y borrarlos).
            AjustesAnteriores = manuales.Where(t => t.Mes < desde).ToList(),
            MesesUsdSinCotizacion = mesesUsd.Where(m => tabla.Para(m) == null).OrderByDescending(m => m).ToList(),
            UltimaActualizacionBcu = bcu.Count > 0 ? bcu.Max(c => c.ActualizadoEn) : null,
            Form = form,
        };
    }
}
