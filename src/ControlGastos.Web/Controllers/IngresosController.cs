using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Controllers;

/// <summary>Ingresos (sueldo, ventas, préstamos devueltos). Cada usuario ve y toca solo los suyos (filtro del DbContext).</summary>
[Route("ingresos")]
[GenerarRecurrentes]
public class IngresosController(IngresoService ingresos, GastoService gastos, AppDbContext db, Reloj reloj) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? mes)
    {
        DateOnly m = Fmt.LeerMes(mes) ?? reloj.MesActual;
        var lista = await ingresos.DelMesAsync(m);
        return View(new IngresosMesVm
        {
            Mes = m,
            EsMesActual = m == reloj.MesActual,
            Lista = lista,
            Balance = IngresoService.Calcular(lista, await gastos.ResumenAsync(m)),
        });
    }

    /// <summary><paramref name="mes"/>: al cargar desde un mes que no es el actual, propone una fecha de ese mes.</summary>
    [HttpGet("nuevo")]
    public IActionResult Nuevo(string? mes)
    {
        DateOnly fecha = Fmt.LeerMes(mes) is DateOnly m && m != reloj.MesActual ? m : reloj.Hoy;
        return View("Form", new IngresoFormVm { Fecha = fecha });
    }

    [HttpPost("nuevo")]
    public async Task<IActionResult> Nuevo(IngresoFormVm vm)
    {
        if (!ModelState.IsValid) return View("Form", vm);
        var ingreso = new Ingreso();
        vm.Aplicar(ingreso);
        db.Ingresos.Add(ingreso);
        await db.SaveChangesAsync();
        TempData["Ok"] = $"Ingreso guardado: {Fmt.Money(ingreso.Monto, ingreso.Moneda)}.";
        return RedirectToAction(nameof(Index), new { mes = Fmt.MesParam(ingreso.Fecha) });
    }

    [HttpGet("{id:int}/editar")]
    public async Task<IActionResult> Editar(int id)
    {
        var i = await db.Ingresos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return i == null ? NotFound() : View("Form", IngresoFormVm.Desde(i));
    }

    [HttpPost("{id:int}/editar")]
    public async Task<IActionResult> Editar(int id, IngresoFormVm vm)
    {
        vm.Id = id;
        var i = await db.Ingresos.FirstOrDefaultAsync(x => x.Id == id);
        if (i == null) return NotFound();
        if (!ModelState.IsValid) return View("Form", vm);
        vm.Aplicar(i);
        await db.SaveChangesAsync();
        TempData["Ok"] = "Ingreso actualizado.";
        return RedirectToAction(nameof(Index), new { mes = Fmt.MesParam(i.Fecha) });
    }

    [HttpPost("{id:int}/eliminar")]
    public async Task<IActionResult> Eliminar(int id)
    {
        var i = await db.Ingresos.FirstOrDefaultAsync(x => x.Id == id);
        if (i == null) return NotFound();
        db.Ingresos.Remove(i);
        await db.SaveChangesAsync();
        TempData["Ok"] = "Ingreso eliminado.";
        return RedirectToAction(nameof(Index), new { mes = Fmt.MesParam(i.Fecha) });
    }
}
