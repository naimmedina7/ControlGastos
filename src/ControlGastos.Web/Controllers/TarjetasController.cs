using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Controllers;

[Route("tarjetas")]
[GenerarRecurrentes]
public class TarjetasController(AppDbContext db, Reloj reloj) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var mes = reloj.MesActual;
        var tarjetas = await db.Tarjetas.AsNoTracking().OrderByDescending(t => t.Activa).ThenBy(t => t.Nombre).ToListAsync();
        var cantidades = await db.Gastos.Where(g => g.TarjetaId != null)
            .GroupBy(g => g.TarjetaId).Select(g => new { Id = g.Key!.Value, N = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.N);
        var delMes = await db.Cuotas.Where(c => c.Periodo == mes && c.Gasto.TarjetaId != null)
            .Select(c => new { Id = c.Gasto.TarjetaId!.Value, c.Monto, c.Gasto.Moneda }).ToListAsync();

        var filas = tarjetas.Select(t => new TarjetaFilaVm
        {
            Tarjeta = t,
            CantidadGastos = cantidades.GetValueOrDefault(t.Id),
            MesUYU = delMes.Where(x => x.Id == t.Id && x.Moneda == Moneda.UYU).Sum(x => x.Monto),
            MesUSD = delMes.Where(x => x.Id == t.Id && x.Moneda == Moneda.USD).Sum(x => x.Monto),
        }).ToList();
        ViewBag.Mes = mes;
        return View(filas);
    }

    [HttpGet("nueva")]
    public IActionResult Nueva() => View("Form", new TarjetaFormVm());

    [HttpPost("nueva")]
    public async Task<IActionResult> Nueva(TarjetaFormVm vm)
    {
        await ValidarNombreAsync(vm);
        if (!ModelState.IsValid) return View("Form", vm);
        db.Tarjetas.Add(new Tarjeta { Nombre = vm.Nombre.Trim(), Tipo = vm.Tipo, Color = vm.Color.ToLowerInvariant(), DiaCierre = Cierre(vm) });
        await db.SaveChangesAsync();
        TempData["Ok"] = $"Tarjeta \"{vm.Nombre.Trim()}\" creada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/editar")]
    public async Task<IActionResult> Editar(int id)
    {
        var t = await db.Tarjetas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        return View("Form", new TarjetaFormVm { Id = t.Id, Nombre = t.Nombre, Tipo = t.Tipo, Color = t.Color, DiaCierre = t.DiaCierre });
    }

    [HttpPost("{id:int}/editar")]
    public async Task<IActionResult> Editar(int id, TarjetaFormVm vm)
    {
        vm.Id = id;
        var t = await db.Tarjetas.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        await ValidarNombreAsync(vm);
        if (!ModelState.IsValid) return View("Form", vm);
        t.Nombre = vm.Nombre.Trim();
        t.Tipo = vm.Tipo;
        t.Color = vm.Color.ToLowerInvariant();
        bool cambioCierre = t.DiaCierre != Cierre(vm);
        t.DiaCierre = Cierre(vm);
        await db.SaveChangesAsync();
        TempData["Ok"] = cambioCierre
            ? "Tarjeta actualizada. El nuevo día de cierre aplica a los gastos que cargues o edites desde ahora."
            : "Tarjeta actualizada.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Activa o desactiva. Una tarjeta inactiva no se ofrece al cargar gastos.</summary>
    [HttpPost("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id)
    {
        var t = await db.Tarjetas.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        t.Activa = !t.Activa;
        await db.SaveChangesAsync();
        TempData["Ok"] = t.Activa ? $"\"{t.Nombre}\" está activa otra vez." : $"\"{t.Nombre}\" quedó desactivada. Sus gastos se conservan.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/eliminar")]
    public async Task<IActionResult> Eliminar(int id)
    {
        var t = await db.Tarjetas.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (await db.Gastos.AnyAsync(g => g.TarjetaId == id))
        {
            TempData["Error"] = $"\"{t.Nombre}\" tiene gastos asociados. Puedes desactivarla para que no aparezca al cargar gastos.";
            return RedirectToAction(nameof(Index));
        }
        if (await db.GastosRecurrentes.AnyAsync(r => r.TarjetaId == id))
        {
            TempData["Error"] = $"\"{t.Nombre}\" la usa un gasto fijo. Cambia el medio de pago de ese gasto fijo o desactiva la tarjeta.";
            return RedirectToAction(nameof(Index));
        }
        db.Tarjetas.Remove(t);
        await db.SaveChangesAsync();
        TempData["Ok"] = "Tarjeta eliminada.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>El día de cierre solo tiene sentido en tarjetas de crédito.</summary>
    private static int? Cierre(TarjetaFormVm vm) => vm.Tipo == TipoTarjeta.Credito ? vm.DiaCierre : null;

    private async Task ValidarNombreAsync(TarjetaFormVm vm)
    {
        string nombre = (vm.Nombre ?? "").Trim();
        if (nombre.Length > 0 && await db.Tarjetas.AnyAsync(t => t.Nombre == nombre && t.Id != vm.Id))
            ModelState.AddModelError(nameof(vm.Nombre), "Ya tienes una tarjeta con ese nombre.");
        if (!Enum.IsDefined(vm.Tipo)) ModelState.AddModelError(nameof(vm.Tipo), "Tipo inválido.");
    }
}
