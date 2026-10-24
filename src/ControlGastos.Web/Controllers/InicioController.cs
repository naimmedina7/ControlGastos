using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlGastos.Web.Controllers;

/// <summary>Rutas públicas: portada y páginas de error.</summary>
[AllowAnonymous]
public class InicioController(IConfiguration config) : Controller
{
    /// <summary>La portada es el inicio de sesión: un paso menos para entrar. El formulario envía a /cuenta/login.</summary>
    [HttpGet("/")]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Panel");
        ViewBag.RegistroHabilitado = config.GetValue("App:RegistroHabilitado", true);
        ViewData["TituloCompleto"] = "Mis·Gastos · Control de gastos personales";
        return View("~/Views/Cuenta/Login.cshtml", new LoginVm());
    }

    // Sin verbo: la página de error se re-ejecuta con el método original (por ejemplo, un POST rechazado por antiforgery).
    [Route("/error/{code:int?}")]
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? code)
    {
        ViewBag.Code = code ?? 500;
        return View();
    }
}
