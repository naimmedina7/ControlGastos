using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Controllers;

[Route("fijos")]
[GenerarRecurrentes]
public class RecurrentesController(RecurrenteService recurrentes, AppDbContext db, Reloj reloj) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var lista = await db.GastosRecurrentes.AsNoTracking()
            .Include(r => r.Tarjeta).Include(r => r.Categoria)
            .ToListAsync();
        return View(new RecurrentesVm
        {
            MesActual = reloj.MesActual,
            Lista = lista.OrderByDescending(r => r.Activo).ThenBy(r => r.DiaDelMes).ThenBy(r => r.Descripcion).ToList(),
        });
    }

    [HttpGet("nuevo")]
    public async Task<IActionResult> Nuevo()
    {
        var vm = new RecurrenteFormVm { DesdeMes = Fmt.MesParam(reloj.MesActual), DiaDelMes = reloj.Hoy.Day };
        await CargarListasAsync(vm);
        return View("Form", vm);
    }

    [HttpPost("nuevo")]
    public async Task<IActionResult> Nuevo(RecurrenteFormVm vm)
    {
        if (ModelState.IsValid && vm.ToInput() is RecurrenteInput input)
        {
            try
            {
                var r = await recurrentes.CrearAsync(input, reloj.MesActual);
                TempData["Ok"] = r.Desde <= reloj.MesActual
                    ? $"Gasto fijo \"{r.Descripcion}\" creado. Ya se cargó en los meses desde {Fmt.MesAnio(r.Desde)}."
                    : $"Gasto fijo \"{r.Descripcion}\" creado. Se cargará desde {Fmt.MesAnio(r.Desde)}.";
                return RedirectToAction(nameof(Index));
            }
            catch (ValidacionException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await CargarListasAsync(vm);
        return View("Form", vm);
    }

    [HttpGet("{id:int}/editar")]
    public async Task<IActionResult> Editar(int id)
    {
        var r = await db.GastosRecurrentes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        var vm = RecurrenteFormVm.Desde(r);
        await CargarListasAsync(vm);
        return View("Form", vm);
    }

    [HttpPost("{id:int}/editar")]
    public async Task<IActionResult> Editar(int id, RecurrenteFormVm vm)
    {
        vm.Id = id;
        if (ModelState.IsValid && vm.ToInput() is RecurrenteInput input)
        {
            try
            {
                if (!await recurrentes.ActualizarAsync(id, input, reloj.MesActual)) return NotFound();
                TempData["Ok"] = "Gasto fijo actualizado. Los cambios aplican a los próximos meses; los ya cargados no se modifican.";
                return RedirectToAction(nameof(Index));
            }
            catch (ValidacionException ex) { ModelState.AddModelError("", ex.Message); }
        }
        await CargarListasAsync(vm);
        return View("Form", vm);
    }

    [HttpPost("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id)
    {
        var r = await recurrentes.CambiarEstadoAsync(id, reloj.MesActual);
        if (r == null) return NotFound();
        TempData["Ok"] = r.Activo ? $"\"{r.Descripcion}\" se reanudó." : $"\"{r.Descripcion}\" quedó en pausa: no se cargará en los próximos meses.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/eliminar")]
    public async Task<IActionResult> Eliminar(int id)
    {
        var n = await recurrentes.EliminarAsync(id);
        if (n == null) return NotFound();
        TempData["Ok"] = n > 0
            ? $"Gasto fijo eliminado. Los {n} gasto(s) ya cargados se conservan."
            : "Gasto fijo eliminado.";
        return RedirectToAction(nameof(Index));
    }

    private async Task CargarListasAsync(RecurrenteFormVm vm)
    {
        vm.Tarjetas = await db.Tarjetas.AsNoTracking().Where(t => t.Activa || t.Id == vm.TarjetaId).OrderBy(t => t.Nombre).ToListAsync();
        vm.Categorias = await db.Categorias.AsNoTracking().OrderBy(c => c.Nombre).ToListAsync();
    }
}
