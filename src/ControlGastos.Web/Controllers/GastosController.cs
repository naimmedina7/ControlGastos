using ControlGastos.Web.Data;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Controllers;

[Route("gastos")]
[GenerarRecurrentes]
public class GastosController(GastoService gastos, RecurrenteService recurrentes, AppDbContext db, Reloj reloj) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? mes)
    {
        DateOnly m = Fmt.LeerMes(mes) ?? reloj.MesActual;
        return View(new GastosMesVm
        {
            Mes = m,
            EsMesActual = m == reloj.MesActual,
            Resumen = await gastos.ResumenAsync(m),
            Previstos = await recurrentes.PrevistosAsync(m),
        });
    }

    /// <summary>Compras en cuotas que todavía tienen cuotas por pagar.</summary>
    [HttpGet("cuotas")]
    public async Task<IActionResult> Cuotas()
    {
        var mes = reloj.MesActual;
        return View(new PlanesVm { Mes = mes, Planes = await gastos.PlanesActivosAsync(mes) });
    }

    /// <summary>
    /// Formulario de alta. <paramref name="tarjeta"/> y <paramref name="fecha"/> vienen de "Guardar y cargar otro",
    /// para cargar varios gastos seguidos del mismo estado de cuenta.
    /// </summary>
    [HttpGet("nuevo")]
    public async Task<IActionResult> Nuevo(string? tarjeta, DateOnly? fecha)
    {
        int? tarjetaId;
        if (tarjeta == "ninguna") tarjetaId = null;
        else if (int.TryParse(tarjeta, out int t)) tarjetaId = t;
        // Sin indicación, propone la tarjeta del último gasto cargado a mano.
        else tarjetaId = await db.Gastos.Where(g => g.GastoRecurrenteId == null)
            .OrderByDescending(g => g.CreadoEn).Select(g => g.TarjetaId).FirstOrDefaultAsync();

        var vm = new GastoFormVm { Fecha = fecha ?? reloj.Hoy, TarjetaId = tarjetaId };
        await CargarListasAsync(vm, conSugerencias: true);
        return View("Form", vm);
    }

    [HttpPost("nuevo")]
    public async Task<IActionResult> Nuevo(GastoFormVm vm, string? otro)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var g = await gastos.CrearAsync(vm.ToInput());
                TempData["Ok"] = g.EnCuotas
                    ? $"Gasto guardado en {g.CantidadCuotas} cuotas, de {Fmt.MesAnio(g.PrimerPeriodo)} a {Fmt.MesAnio(g.UltimoPeriodo)}."
                    : "Gasto guardado.";
                if (otro == "1")
                    return RedirectToAction(nameof(Nuevo), new { tarjeta = g.TarjetaId?.ToString() ?? "ninguna", fecha = vm.Fecha.ToString("yyyy-MM-dd") });
                // Se vuelve al mes en que impacta la cuota cargada (puede ser el siguiente por el día de cierre).
                return RedirectToAction(nameof(Index), new { mes = Fmt.MesParam(g.PrimerPeriodo.AddMonths(vm.ToInput().CuotaActual - 1)) });
            }
            catch (ValidacionException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await CargarListasAsync(vm, conSugerencias: true);
        return View("Form", vm);
    }

    [HttpGet("{id:int}/editar")]
    public async Task<IActionResult> Editar(int id)
    {
        var g = await db.Gastos.AsNoTracking().Include(x => x.Tarjeta).Include(x => x.GastoRecurrente).FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound(); // también si el gasto es de otro usuario
        var vm = GastoFormVm.Desde(g);
        await CargarListasAsync(vm);
        return View("Form", vm);
    }

    [HttpPost("{id:int}/editar")]
    public async Task<IActionResult> Editar(int id, GastoFormVm vm)
    {
        vm.Id = id;
        if (ModelState.IsValid)
        {
            try
            {
                if (!await gastos.ActualizarAsync(id, vm.ToInput())) return NotFound();
                TempData["Ok"] = "Gasto actualizado.";
                var periodo = await db.Gastos.Where(x => x.Id == id).Select(x => x.PrimerPeriodo).FirstAsync();
                return RedirectToAction(nameof(Index), new { mes = Fmt.MesParam(periodo.AddMonths(vm.ToInput().CuotaActual - 1)) });
            }
            catch (ValidacionException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await CargarListasAsync(vm);
        return View("Form", vm);
    }

    [HttpPost("{id:int}/eliminar")]
    public async Task<IActionResult> Eliminar(int id, string? mes)
    {
        if (!await gastos.EliminarAsync(id)) return NotFound();
        TempData["Ok"] = "Gasto eliminado, junto con todas sus cuotas.";
        return RedirectToAction(nameof(Index), new { mes = Fmt.LeerMes(mes) is DateOnly m ? Fmt.MesParam(m) : null });
    }

    private async Task CargarListasAsync(GastoFormVm vm, bool conSugerencias = false)
    {
        if (conSugerencias)
        {
            // Últimos 300 gastos cargados a mano: gana la descripción más reciente, con su categoría, tarjeta y moneda.
            var recientes = await db.Gastos.AsNoTracking()
                .Where(g => g.GastoRecurrenteId == null)
                .OrderByDescending(g => g.CreadoEn).Take(300)
                .Select(g => new SugerenciaGasto(g.Descripcion, g.CategoriaId, g.TarjetaId, g.Moneda))
                .ToListAsync();
            vm.Sugerencias = recientes.DistinctBy(x => x.Descripcion.ToLowerInvariant()).Take(80).ToList();
        }
        // Las tarjetas inactivas solo aparecen si el gasto ya la usaba.
        vm.Tarjetas = await db.Tarjetas.AsNoTracking()
            .Where(t => t.Activa || t.Id == vm.TarjetaId)
            .OrderBy(t => t.Nombre).ToListAsync();
        vm.Categorias = await db.Categorias.AsNoTracking().OrderBy(c => c.Nombre).ToListAsync();
    }
}
