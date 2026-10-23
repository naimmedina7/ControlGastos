using ControlGastos.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ControlGastos.Web.Infrastructure;

/// <summary>
/// Antes de mostrar pantallas con totales, genera los gastos fijos del mes en curso que falten.
/// Así no hace falta un proceso en segundo plano: el mes se completa la primera vez que entras.
/// </summary>
public class GenerarRecurrentesAttribute() : TypeFilterAttribute(typeof(Filtro))
{
    private class Filtro(RecurrenteService recurrentes, Reloj reloj) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.HttpContext.User.Identity?.IsAuthenticated == true && HttpMethods.IsGet(context.HttpContext.Request.Method))
                await recurrentes.GenerarPendientesAsync(reloj.MesActual);
            await next();
        }
    }
}
