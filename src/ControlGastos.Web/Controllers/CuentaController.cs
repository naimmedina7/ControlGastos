using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Controllers;

[Route("cuenta")]
public class CuentaController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    AppDbContext db,
    IConfiguration config,
    ILogger<CuentaController> logger) : Controller
{
    private bool RegistroHabilitado => config.GetValue("App:RegistroHabilitado", true);

    /// <summary>
    /// Correos que pueden crear cuenta (App:CorreosPermitidos, separados por coma o punto y coma).
    /// Vacío = cualquiera. Pensado para una app de uso personal o familiar publicada en internet.
    /// </summary>
    private bool CorreoPermitido(string email)
    {
        var lista = (config["App:CorreosPermitidos"] ?? "")
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lista.Length == 0 || lista.Contains(email, StringComparer.OrdinalIgnoreCase);
    }

    // ---------- Rutas públicas ----------

    [AllowAnonymous, HttpGet("login")]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Panel");
        ViewBag.RegistroHabilitado = RegistroHabilitado;
        return View(new LoginVm { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        ViewBag.RegistroHabilitado = RegistroHabilitado;
        if (!ModelState.IsValid) return View(vm);

        var result = await signIn.PasswordSignInAsync(vm.Email.Trim(), vm.Password, vm.Recordarme, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            // Solo se redirige a URLs propias (evita open redirect).
            return Url.IsLocalUrl(vm.ReturnUrl) ? LocalRedirect(vm.ReturnUrl!) : RedirectToAction("Index", "Panel");
        }

        // Mensaje genérico: no revela si el correo existe.
        ModelState.AddModelError("", result.IsLockedOut
            ? "Demasiados intentos fallidos. Vuelve a intentar en unos minutos."
            : "Correo o contraseña incorrectos.");
        vm.Password = "";
        return View(vm);
    }

    [AllowAnonymous, HttpGet("registro")]
    public IActionResult Registro()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Panel");
        if (!RegistroHabilitado) return View("RegistroCerrado");
        return View(new RegistroVm());
    }

    [AllowAnonymous, HttpPost("registro"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Registro(RegistroVm vm)
    {
        if (!RegistroHabilitado) return View("RegistroCerrado");
        if (!ModelState.IsValid) return View(vm);

        string email = vm.Email.Trim();
        if (!CorreoPermitido(email))
        {
            logger.LogWarning("Registro rechazado: correo no habilitado");
            ModelState.AddModelError("", "Este correo no está habilitado para crear una cuenta. Pídele acceso a quien administra la app.");
            vm.Password = vm.ConfirmarPassword = "";
            return View(vm);
        }
        var user = new AppUser { UserName = email, Email = email, Nombre = vm.Nombre.Trim() };
        var result = await users.CreateAsync(user, vm.Password); // Identity guarda solo el hash
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            vm.Password = vm.ConfirmarPassword = "";
            return View(vm);
        }

        // Todavía no hay sesión, así que el UserId se asigna explícitamente.
        db.Categorias.AddRange(CategoriasPorDefecto.Crear(user.Id));
        await db.SaveChangesAsync();

        logger.LogInformation("Nueva cuenta {UserId}", user.Id);
        await signIn.SignInAsync(user, isPersistent: false);
        TempData["Ok"] = "Tu cuenta está lista. Empieza creando una tarjeta.";
        return RedirectToAction("Index", "Tarjetas");
    }

    // ---------- Rutas privadas ----------

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("perfil")]
    public async Task<IActionResult> Perfil() => View(await ArmarPerfilAsync());

    [HttpPost("perfil/contrasena"), EnableRateLimiting("auth")]
    public async Task<IActionResult> CambiarPassword([Bind(Prefix = "Password")] CambiarPasswordVm vm)
    {
        if (!ModelState.IsValid) return View("Perfil", await ArmarPerfilAsync(vm));

        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await users.ChangePasswordAsync(user, vm.Actual, vm.Nueva);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View("Perfil", await ArmarPerfilAsync(new CambiarPasswordVm()));
        }

        // Cambia el security stamp: se cierran las sesiones abiertas en otros dispositivos.
        await signIn.RefreshSignInAsync(user);
        TempData["Ok"] = "Contraseña actualizada.";
        return RedirectToAction(nameof(Perfil));
    }

    // ---------- Respaldo ----------

    /// <summary>Descarga todos los datos del usuario en un archivo JSON.</summary>
    [HttpGet("respaldo")]
    public async Task<IActionResult> DescargarRespaldo([FromServices] RespaldoService respaldos, [FromServices] Reloj reloj)
    {
        var bytes = await respaldos.ExportarAsync(DateTime.UtcNow);
        logger.LogInformation("Respaldo descargado por {UserId}", users.GetUserId(User));
        return File(bytes, "application/json", $"mis-gastos-respaldo-{reloj.Hoy:yyyy-MM-dd}.json");
    }

    /// <summary>
    /// Reemplaza todos los datos del usuario por los del archivo. Es destructivo, así que pide la contraseña
    /// y una confirmación explícita; el archivo se valida completo antes de tocar nada.
    /// </summary>
    [HttpPost("respaldo/restaurar"), EnableRateLimiting("auth")]
    [RequestSizeLimit(RespaldoService.MaxBytes + 100_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = RespaldoService.MaxBytes + 100_000)]
    public async Task<IActionResult> RestaurarRespaldo(IFormFile? archivo, string? passwordActual, bool confirmo,
        [FromServices] RespaldoService respaldos)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();

        string? error = null;
        if (archivo == null || archivo.Length == 0) error = "Elige el archivo de respaldo.";
        else if (archivo.Length > RespaldoService.MaxBytes) error = "El archivo es demasiado grande.";
        else if (!confirmo) error = "Marca la casilla para confirmar que quieres reemplazar tus datos.";
        else if (string.IsNullOrEmpty(passwordActual)
                 || !(await signIn.CheckPasswordSignInAsync(user, passwordActual, lockoutOnFailure: true)).Succeeded)
            error = "La contraseña no es correcta.";
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Perfil));
        }

        try
        {
            Respaldo r;
            await using (var stream = archivo!.OpenReadStream()) r = RespaldoService.Leer(stream);
            var res = await respaldos.RestaurarAsync(r);
            logger.LogInformation("Respaldo restaurado por {UserId}: {Gastos} gastos", user.Id, res.Gastos);
            TempData["Ok"] = $"Respaldo restaurado: {res.Gastos} gastos, {res.GastosFijos} gastos fijos, {res.Tarjetas} tarjetas y {res.Categorias} categorías.";
        }
        catch (ValidacionException ex)
        {
            TempData["Error"] = ex.Message + " No se modificó nada.";
        }
        return RedirectToAction(nameof(Perfil));
    }

    private async Task<PerfilVm> ArmarPerfilAsync(CambiarPasswordVm? password = null)
    {
        var user = await users.GetUserAsync(User) ?? throw new InvalidOperationException("Sesión sin usuario.");
        return new PerfilVm
        {
            Nombre = user.Nombre,
            Email = user.Email ?? "",
            CreadoEn = user.CreadoEn,
            CantidadGastos = await db.Gastos.CountAsync(),
            CantidadTarjetas = await db.Tarjetas.CountAsync(),
            Password = password ?? new CambiarPasswordVm(),
        };
    }
}
