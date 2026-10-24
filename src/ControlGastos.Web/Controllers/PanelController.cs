using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ControlGastos.Web.Controllers;

[Route("panel")]
[GenerarRecurrentes]
public class PanelController(GastoService gastos, PresupuestoService presupuestos, IngresoService ingresos, Reloj reloj, UserManager<AppUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? mes)
    {
        DateOnly m = Fmt.LeerMes(mes) ?? reloj.MesActual;
        var user = await users.GetUserAsync(User);
        var resumen = await gastos.ResumenAsync(m);
        return View(new PanelVm
        {
            Nombre = user?.Nombre ?? "",
            Mes = m,
            EsMesActual = m == reloj.MesActual,
            Resumen = resumen,
            Balance = IngresoService.Calcular(await ingresos.DelMesAsync(m), resumen),
            MesAnterior = await gastos.ResumenAsync(m.AddMonths(-1)),
            Proximos = await gastos.ProyeccionAsync(m, 6),
            Presupuestos = await presupuestos.EstadoAsync(m),
        });
    }
}
