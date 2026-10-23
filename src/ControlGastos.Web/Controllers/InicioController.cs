using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlGastos.Web.Controllers;

/// <summary>Rutas públicas: portada y páginas de error.</summary>
[AllowAnonymous]
public class InicioController : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Panel");
        return View();
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
