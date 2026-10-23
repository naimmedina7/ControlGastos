using ControlGastos.Web.Data;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Controllers;

[Route("presupuestos")]
[GenerarRecurrentes]
public class PresupuestosController(PresupuestoService presupuestos, AppDbContext db, Reloj reloj) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await ArmarAsync(new PresupuestoFormVm()));

    [HttpPost("")]
    public async Task<IActionResult> Guardar([Bind(Prefix = "Form")] PresupuestoFormVm form)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await presupuestos.GuardarAsync(form.CategoriaId!.Value, form.Moneda, form.MontoMensual);
                TempData["Ok"] = "Presupuesto guardado.";
                return RedirectToAction(nameof(Index));
            }
            catch (ValidacionException ex) { ModelState.AddModelError("", ex.Message); }
        }
        return View("Index", await ArmarAsync(form));
    }

    [HttpPost("{id:int}/eliminar")]
    public async Task<IActionResult> Eliminar(int id)
    {
        if (!await presupuestos.EliminarAsync(id)) return NotFound();
        TempData["Ok"] = "Presupuesto eliminado.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<PresupuestosVm> ArmarAsync(PresupuestoFormVm form) => new()
    {
        Mes = reloj.MesActual,
        Estados = await presupuestos.EstadoAsync(reloj.MesActual),
        Categorias = await db.Categorias.AsNoTracking().OrderBy(c => c.Nombre).ToListAsync(),
        Form = form,
    };
}
