using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Controllers;

/// <summary>
/// Importar un estado de cuenta en dos pasos: 1) pegar el texto y ver la vista previa; 2) confirmar.
/// Al confirmar se vuelve a interpretar el texto en el servidor: del formulario solo se toman las
/// decisiones del usuario (incluir, descripción, categoría), nunca montos ni fechas.
/// </summary>
[Route("importar")]
public class ImportarController(ImportacionService importacion, AppDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var vm = new ImportarVm();
        await CargarListasAsync(vm);
        vm.TarjetaId = vm.Tarjetas.FirstOrDefault(t => t.Tipo == TipoTarjeta.Credito)?.Id ?? vm.Tarjetas.FirstOrDefault()?.Id;
        return View(vm);
    }

    [HttpPost("analizar")]
    public async Task<IActionResult> Analizar(ImportarVm vm)
    {
        await CargarListasAsync(vm);
        if (vm.TarjetaId is int t && vm.Tarjetas.All(x => x.Id != t))
            ModelState.AddModelError(nameof(vm.TarjetaId), "La tarjeta seleccionada no existe.");
        if (!ModelState.IsValid) return View("Index", vm);

        vm.Analisis = await importacion.AnalizarAsync(vm.Texto, vm.TarjetaId, Fmt.LeerMes(vm.MesEstado));
        vm.MesEstado = Fmt.MesParam(vm.Analisis.MesEstado);
        if (!vm.Analisis.Gastos.Any())
            ModelState.AddModelError("", "No encontré gastos en el texto. Revisa que cada línea tenga fecha, moneda y monto.");
        return View("Index", vm);
    }

    [HttpPost("confirmar")]
    // Hasta 400 líneas × 4 campos: más que el límite por defecto de 1024 valores por formulario.
    [RequestFormLimits(ValueCountLimit = 4096)]
    public async Task<IActionResult> Confirmar(ConfirmarImportacionVm vm)
    {
        var mes = Fmt.LeerMes(vm.MesEstado);
        if (!ModelState.IsValid || mes == null)
        {
            TempData["Error"] = "No se pudo leer la importación. Vuelve a pegar el texto.";
            return RedirectToAction(nameof(Index));
        }
        if (vm.TarjetaId is int t && !await db.Tarjetas.AnyAsync(x => x.Id == t))
            return NotFound();

        var decisiones = vm.Items.GroupBy(i => i.Numero).ToDictionary(g => g.Key, g => g.First());
        var aImportar = EstadoDeCuenta.Interpretar(vm.Texto)
            .Where(l => l.Tipo == TipoLinea.Gasto && decisiones.TryGetValue(l.Numero, out var d) && d.Incluir)
            .Select(l =>
            {
                var d = decisiones[l.Numero];
                return new LineaAImportar(l.Comercio, l.Fecha, l.Moneda, l.Monto, l.CuotaActual, l.CantidadCuotas, d.Descripcion ?? "", d.CategoriaId);
            })
            .ToList();

        if (aImportar.Count == 0)
        {
            TempData["Error"] = "No marcaste ninguna línea para importar.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            int n = await importacion.ImportarAsync(vm.TarjetaId, mes.Value, aImportar);
            TempData["Ok"] = $"Se importaron {n} gasto(s) del estado de cuenta de {Fmt.MesAnio(mes.Value)}.";
            return RedirectToAction("Index", "Gastos", new { mes = Fmt.MesParam(mes.Value) });
        }
        catch (ValidacionException ex)
        {
            TempData["Error"] = $"No se importó nada: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task CargarListasAsync(ImportarVm vm)
    {
        vm.Tarjetas = await db.Tarjetas.AsNoTracking().Where(t => t.Activa || t.Id == vm.TarjetaId)
            .OrderByDescending(t => t.Tipo == TipoTarjeta.Credito).ThenBy(t => t.Nombre).ToListAsync();
        vm.Categorias = await db.Categorias.AsNoTracking().OrderBy(c => c.Nombre).ToListAsync();
    }
}
