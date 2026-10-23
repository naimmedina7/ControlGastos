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
public class PanelController(GastoService gastos, PresupuestoService presupuestos, Reloj reloj, UserManager<AppUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? mes)
    {
        DateOnly m = Fmt.LeerMes(mes) ?? reloj.MesActual;
        var user = await users.GetUserAsync(User);
        return View(new PanelVm
        {
            Nombre = user?.Nombre ?? "",
            Mes = m,
            EsMesActual = m == reloj.MesActual,
            Resumen = await gastos.ResumenAsync(m),
            MesAnterior = await gastos.ResumenAsync(m.AddMonths(-1)),
            Proximos = await gastos.ProyeccionAsync(m, 6),
            Presupuestos = await presupuestos.EstadoAsync(m),
        });
    }
}
