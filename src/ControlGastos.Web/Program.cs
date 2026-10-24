using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;

// Los formularios envían números con punto y fechas ISO: se usa cultura invariante para el binding.
// El formato visible (es-UY) lo resuelve Helpers/Fmt.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Vercel (y otros hostings de contenedores) indican el puerto en la variable PORT. Solo fuera de desarrollo:
// en la PC se usa launchSettings (localhost), así la app no queda expuesta a toda la red local.
if (!builder.Environment.IsDevelopment()
    && Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } puerto && int.TryParse(puerto, out _))
    builder.WebHost.UseUrls($"http://0.0.0.0:{puerto}");

// ---------- Base de datos ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUsuarioActual, UsuarioActualHttp>();
builder.Services.AddBaseDeDatos(config, builder.Environment.ContentRootPath);

// ---------- Identity: usuarios y contraseñas con hash (PBKDF2 + salt) ----------
builder.Services
    .AddIdentity<AppUser, IdentityRole>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.SignIn.RequireConfirmedAccount = false; // Pasa a true cuando haya envío de emails (etapa 5).
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<ErroresIdentityEnEspanol>();

// PBKDF2-HMAC-SHA512 con 210.000 iteraciones (recomendación de OWASP). Los hashes viejos (100.000)
// se siguen aceptando y se rehashean solos en el próximo login.
builder.Services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000);

// Cada 5 minutos se revalida la sesión contra la base: al cambiar la contraseña, las sesiones
// abiertas en otros dispositivos se cierran en ese plazo (por defecto eran 30 minutos).
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

// ---------- Token de sesión: cookie cifrada y firmada ----------
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = ".ControlGastos.Auth";
    o.Cookie.HttpOnly = true;                                 // JavaScript no la puede leer
    o.Cookie.SameSite = SameSiteMode.Lax;                     // no viaja en POST desde otros sitios
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;                          // solo por HTTPS en producción
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    o.LoginPath = "/cuenta/login";
    o.LogoutPath = "/cuenta/logout";
    o.AccessDeniedPath = "/cuenta/login";
});

// Las claves que cifran la cookie se guardan en la base: sobreviven a reinicios y redeploys.
builder.Services.AddDataProtection()
    .SetApplicationName("ControlGastos")
    .PersistKeysToDbContext<AppDbContext>();

// ---------- Límite de intentos en login y registro ----------
int intentosPorMinuto = config.GetValue("Seguridad:IntentosPorMinuto", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = intentosPorMinuto, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// ---------- MVC: todo privado salvo [AllowAnonymous]; antiforgery en todos los POST ----------
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddRouting(o => o.LowercaseUrls = true);
// Razor escapa igual "<", ">" y comillas, pero deja tildes y ñ legibles en el HTML.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Reloj>();
builder.Services.AddScoped<GastoService>();
builder.Services.AddScoped<RecurrenteService>();
builder.Services.AddScoped<ImportacionService>();
builder.Services.AddScoped<ReporteService>();
builder.Services.AddScoped<PresupuestoService>();
builder.Services.AddScoped<RespaldoService>();
builder.Services.AddScoped<IngresoService>();
// Cotización del dólar (BCU vía Frankfurter): gratuita, sin clave; con timeout corto para no trabar la página.
builder.Services.AddHttpClient<CotizacionService>(c =>
{
    c.BaseAddress = new Uri(config["Cotizaciones:Url"] ?? "https://api.frankfurter.dev/");
    c.Timeout = TimeSpan.FromSeconds(6);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("MisGastos/1.0 (+uso personal)");
});

// Detrás del proxy del hosting (Vercel, Render) la request llega por HTTP: se confía en X-Forwarded-*.
// Se toma solo el último valor (ForwardLimit = 1), que es el que agrega el proxy; Vercel además
// reemplaza X-Forwarded-For con la IP real del cliente, así que no se puede falsificar.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// ---------- Modo administración por consola (no levanta el servidor web) ----------
if (ComandosAdmin.EsComando(args))
{
    Environment.ExitCode = await ComandosAdmin.EjecutarAsync(app.Services, args, Console.Out);
    return;
}

// ---------- Migraciones y datos de ejemplo ----------
if (config.GetValue("Database:MigrarAlIniciar", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational()) await db.Database.MigrateAsync();
}
if (app.Environment.IsDevelopment())
{
    await DatosDeEjemplo.CargarAsync(app.Services, config, app.Logger);
}

// ---------- Pipeline ----------
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseEncabezadosSeguridad();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Inicio}/{action=Index}/{id?}")
   .WithStaticAssets();

app.Run();

// Necesario para los tests de integración (WebApplicationFactory<Program>).
public partial class Program;
