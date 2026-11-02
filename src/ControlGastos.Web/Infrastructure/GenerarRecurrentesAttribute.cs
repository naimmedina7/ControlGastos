using ControlGastos.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ControlGastos.Web.Infrastructure;

/// <summary>
/// Antes de mostrar pantallas con totales, genera los gastos fijos que falten hasta el mes en curso o,
/// si la pantalla muestra un mes futuro (<c>?mes=2027-02</c>), hasta ese mes.
/// Así no hace falta un proceso en segundo plano: cada mes se completa la primera vez que lo miras.
/// </summary>
public class GenerarRecurrentesAttribute() : TypeFilterAttribute(typeof(Filtro))
{
    private class Filtro(RecurrenteService recurrentes, Reloj reloj) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.HttpContext.User.Identity?.IsAuthenticated == true && HttpMethods.IsGet(context.HttpContext.Request.Method))
                await recurrentes.GenerarPendientesAsync(
                    RecurrenteService.HastaQueMes(reloj.MesActual, Fmt.LeerMes(context.HttpContext.Request.Query["mes"])));
            await next();
        }
    }
}
