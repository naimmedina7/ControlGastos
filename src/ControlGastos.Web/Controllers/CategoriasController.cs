using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Controllers;

[Route("categorias")]
public class CategoriasController(AppDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var categorias = await db.Categorias.AsNoTracking().OrderBy(c => c.Nombre).ToListAsync();
        var cantidades = await db.Gastos.Where(g => g.CategoriaId != null)
            .GroupBy(g => g.CategoriaId).Select(g => new { Id = g.Key!.Value, N = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.N);
        return View(categorias.Select(c => new CategoriaFilaVm { Categoria = c, CantidadGastos = cantidades.GetValueOrDefault(c.Id) }).ToList());
    }

    [HttpGet("nueva")]
    public IActionResult Nueva() => View("Form", new CategoriaFormVm());

    [HttpPost("nueva")]
    public async Task<IActionResult> Nueva(CategoriaFormVm vm)
    {
        await ValidarNombreAsync(vm);
        if (!ModelState.IsValid) return View("Form", vm);
        db.Categorias.Add(new Categoria { Nombre = vm.Nombre.Trim(), Icono = vm.Icono, Color = vm.Color.ToLowerInvariant() });
        await db.SaveChangesAsync();
        TempData["Ok"] = $"Categoría \"{vm.Nombre.Trim()}\" creada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/editar")]
    public async Task<IActionResult> Editar(int id)
    {
        var c = await db.Categorias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        return View("Form", new CategoriaFormVm { Id = c.Id, Nombre = c.Nombre, Icono = c.Icono, Color = c.Color });
    }

    [HttpPost("{id:int}/editar")]
    public async Task<IActionResult> Editar(int id, CategoriaFormVm vm)
    {
        vm.Id = id;
        var c = await db.Categorias.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        await ValidarNombreAsync(vm);
        if (!ModelState.IsValid) return View("Form", vm);
        c.Nombre = vm.Nombre.Trim();
        c.Icono = vm.Icono;
        c.Color = vm.Color.ToLowerInvariant();
        await db.SaveChangesAsync();
        TempData["Ok"] = "Categoría actualizada.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Los gastos de la categoría quedan "sin categoría"; no se borran.</summary>
    [HttpPost("{id:int}/eliminar")]
    public async Task<IActionResult> Eliminar(int id)
    {
        var c = await db.Categorias.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        // Se desvinculan a mano en lugar de depender del SET NULL de la base, para que funcione igual en todos los motores.
        var gastos = await db.Gastos.Where(g => g.CategoriaId == id).ToListAsync();
        foreach (var g in gastos) g.CategoriaId = null;
        db.Categorias.Remove(c);
        await db.SaveChangesAsync();
        TempData["Ok"] = gastos.Count > 0
            ? $"Categoría eliminada. {gastos.Count} gasto(s) quedaron sin categoría."
            : "Categoría eliminada.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidarNombreAsync(CategoriaFormVm vm)
    {
        string nombre = (vm.Nombre ?? "").Trim();
        if (nombre.Length > 0 && await db.Categorias.AnyAsync(c => c.Nombre == nombre && c.Id != vm.Id))
            ModelState.AddModelError(nameof(vm.Nombre), "Ya tienes una categoría con ese nombre.");
    }
}
