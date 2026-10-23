using System.Net;
using System.Text.RegularExpressions;
using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ControlGastos.Tests;

/// <summary>Levanta la app real (en memoria, con SQLite en un archivo temporal) y la prueba por HTTP.</summary>
public class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"control-gastos-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_db};Pooling=False");
        builder.UseSetting("Seguridad:IntentosPorMinuto", "1000");
        builder.UseSetting("App:RegistroHabilitado", "true");
        builder.UseSetting("Cotizaciones:Habilitado", "false"); // los tests no salen a internet
    }

    public HttpClient Cliente() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_db); } catch (IOException) { }
    }
}

public class SeguridadHttpTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

    private static async Task<string> TokenAsync(HttpClient c, string url)
    {
        var html = await c.GetStringAsync(url);
        return TokenRegex.Match(html).Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient c, string formUrl, string postUrl, Dictionary<string, string> datos)
    {
        datos["__RequestVerificationToken"] = await TokenAsync(c, formUrl);
        return await c.PostAsync(postUrl, new FormUrlEncodedContent(datos));
    }

    private static string EmailNuevo() => $"u{Guid.NewGuid():N}@test.local";

    /// <summary>Registra una cuenta nueva y deja al cliente con la sesión iniciada.</summary>
    private async Task<(HttpClient Cliente, string Email)> RegistrarAsync(string password = "clave1234")
    {
        var c = factory.Cliente();
        string email = EmailNuevo();
        var r = await PostFormAsync(c, "/cuenta/registro", "/cuenta/registro", new()
        {
            ["Nombre"] = "Usuario Test", ["Email"] = email, ["Password"] = password, ["ConfirmarPassword"] = password,
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return (c, email);
    }

    // ---------- Rutas públicas y privadas ----------

    [Theory]
    [InlineData("/")]
    [InlineData("/cuenta/login")]
    [InlineData("/cuenta/registro")]
    public async Task Las_rutas_publicas_responden_sin_sesion(string url)
    {
        var r = await factory.Cliente().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("/panel")]
    [InlineData("/gastos")]
    [InlineData("/gastos/nuevo")]
    [InlineData("/gastos/1/editar")]
    [InlineData("/tarjetas")]
    [InlineData("/tarjetas/nueva")]
    [InlineData("/categorias")]
    [InlineData("/cuenta/perfil")]
    [InlineData("/gastos/cuotas")]
    [InlineData("/fijos")]
    [InlineData("/fijos/nuevo")]
    [InlineData("/fijos/1/editar")]
    [InlineData("/reportes")]
    [InlineData("/reportes/exportar")]
    [InlineData("/importar")]
    [InlineData("/tipo-cambio")]
    [InlineData("/presupuestos")]
    public async Task Las_rutas_privadas_redirigen_al_login_sin_sesion(string url)
    {
        var r = await factory.Cliente().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("https://localhost/cuenta/login", r.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Con_sesion_las_rutas_privadas_responden()
    {
        var (c, _) = await RegistrarAsync();
        foreach (var url in new[] { "/panel", "/gastos", "/gastos/nuevo", "/gastos/cuotas", "/fijos", "/fijos/nuevo", "/tarjetas", "/categorias", "/cuenta/perfil",
                                    "/reportes", "/reportes?vista=Pesificado&rango=anio", "/reportes?desde=2026-01&hasta=2026-06", "/importar", "/tipo-cambio", "/presupuestos" })
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(url)).StatusCode);
    }

    // ---------- Contraseñas y token ----------

    [Fact]
    public async Task La_contrasena_se_guarda_con_hash()
    {
        const string password = "MiClaveSecreta9";
        var (_, email) = await RegistrarAsync(password);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);

        Assert.NotNull(user!.PasswordHash);
        Assert.DoesNotContain(password, user.PasswordHash);
        // Formato v3 de Identity: PBKDF2 con salt aleatorio, codificado en base64.
        Assert.Equal(0x01, Convert.FromBase64String(user.PasswordHash)[0]);
        Assert.True(await users.CheckPasswordAsync(user, password));
    }

    [Fact]
    public async Task La_cookie_de_sesion_es_HttpOnly_Secure_y_SameSite()
    {
        var c = factory.Cliente();
        string email = EmailNuevo();
        var r = await PostFormAsync(c, "/cuenta/registro", "/cuenta/registro", new()
        {
            ["Nombre"] = "Test", ["Email"] = email, ["Password"] = "clave1234", ["ConfirmarPassword"] = "clave1234",
        });
        var cookie = r.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(".ControlGastos.Auth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_correcto_y_logout_cierra_la_sesion()
    {
        var (registrado, email) = await RegistrarAsync();
        var c = factory.Cliente();
        var r = await PostFormAsync(c, "/cuenta/login", "/cuenta/login", new() { ["Email"] = email, ["Password"] = "clave1234" });
        Assert.Equal("/panel", r.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/panel")).StatusCode);

        var salir = await PostFormAsync(c, "/panel", "/cuenta/logout", new());
        Assert.Equal(HttpStatusCode.Redirect, salir.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/panel")).StatusCode);
    }

    [Fact]
    public async Task Login_incorrecto_da_un_mensaje_generico()
    {
        var (_, email) = await RegistrarAsync();
        var c = factory.Cliente();
        var conEmailReal = await PostFormAsync(c, "/cuenta/login", "/cuenta/login", new() { ["Email"] = email, ["Password"] = "otra-clave1" });
        var conEmailFalso = await PostFormAsync(c, "/cuenta/login", "/cuenta/login", new() { ["Email"] = EmailNuevo(), ["Password"] = "otra-clave1" });

        string a = await conEmailReal.Content.ReadAsStringAsync(), b = await conEmailFalso.Content.ReadAsStringAsync();
        Assert.Contains("Correo o contraseña incorrectos.", a);
        Assert.Contains("Correo o contraseña incorrectos.", b);
    }

    [Fact]
    public async Task La_cuenta_se_bloquea_tras_cinco_intentos_fallidos()
    {
        var (_, email) = await RegistrarAsync();
        var c = factory.Cliente();
        for (int i = 0; i < 5; i++)
            await PostFormAsync(c, "/cuenta/login", "/cuenta/login", new() { ["Email"] = email, ["Password"] = "incorrecta1" });

        // Ni siquiera con la contraseña correcta entra mientras dure el bloqueo.
        var r = await PostFormAsync(c, "/cuenta/login", "/cuenta/login", new() { ["Email"] = email, ["Password"] = "clave1234" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Demasiados intentos fallidos", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task El_login_no_redirige_a_sitios_externos()
    {
        var (_, email) = await RegistrarAsync();
        var c = factory.Cliente();
        var r = await PostFormAsync(c, "/cuenta/login", "/cuenta/login", new()
        {
            ["Email"] = email, ["Password"] = "clave1234", ["ReturnUrl"] = "https://sitio-malicioso.example/robar",
        });
        Assert.Equal("/panel", r.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Rechaza_contrasenas_debiles()
    {
        var c = factory.Cliente();
        var r = await PostFormAsync(c, "/cuenta/registro", "/cuenta/registro", new()
        {
            ["Nombre"] = "Test", ["Email"] = EmailNuevo(), ["Password"] = "solotexto", ["ConfirmarPassword"] = "solotexto",
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // vuelve al formulario
        Assert.Contains("al menos un número", await r.Content.ReadAsStringAsync());
    }

    // ---------- Antiforgery y encabezados ----------

    [Fact]
    public async Task Un_POST_sin_token_antiforgery_se_rechaza()
    {
        var (c, _) = await RegistrarAsync();
        var r = await c.PostAsync("/tarjetas/nueva", new FormUrlEncodedContent(new Dictionary<string, string> { ["Nombre"] = "X", ["Tipo"] = "Credito", ["Color"] = "#059669" }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Las_respuestas_incluyen_encabezados_de_seguridad()
    {
        var r = await factory.Cliente().GetAsync("/cuenta/login");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
    }

    // ---------- Aislamiento entre cuentas por HTTP ----------

    [Fact]
    public async Task Un_usuario_no_puede_ver_editar_ni_borrar_gastos_de_otro()
    {
        var (ana, _) = await RegistrarAsync();
        var crear = await PostFormAsync(ana, "/gastos/nuevo", "/gastos/nuevo", new()
        {
            ["Descripcion"] = "Gasto de Ana", ["Fecha"] = "2026-10-10", ["TarjetaId"] = "", ["Moneda"] = "UYU",
            ["MontoCuota"] = "100.50", ["EnCuotas"] = "true", ["CantidadCuotas"] = "3", ["CuotaActual"] = "1",
        });
        Assert.Equal(HttpStatusCode.Redirect, crear.StatusCode);

        string listado = await ana.GetStringAsync("/gastos?mes=2026-10");
        Assert.Contains("Gasto de Ana", listado);
        string id = Regex.Match(listado, "/gastos/(\\d+)/editar").Groups[1].Value;

        var (beto, _) = await RegistrarAsync();
        Assert.DoesNotContain("Gasto de Ana", await beto.GetStringAsync("/gastos?mes=2026-10"));
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync($"/gastos/{id}/editar")).StatusCode);

        var editar = await PostFormAsync(beto, "/gastos/nuevo", $"/gastos/{id}/editar", new()
        {
            ["Descripcion"] = "Hackeado", ["Fecha"] = "2026-10-10", ["Moneda"] = "UYU", ["MontoCuota"] = "1", ["CantidadCuotas"] = "1", ["CuotaActual"] = "1",
        });
        Assert.Equal(HttpStatusCode.NotFound, editar.StatusCode);

        var borrar = await PostFormAsync(beto, "/gastos/nuevo", $"/gastos/{id}/eliminar", new());
        Assert.Equal(HttpStatusCode.NotFound, borrar.StatusCode);

        Assert.Contains("Gasto de Ana", await ana.GetStringAsync("/gastos?mes=2026-10"));
    }

    [Fact]
    public async Task Cada_cuenta_nueva_recibe_sus_categorias()
    {
        var (c, _) = await RegistrarAsync();
        string html = await c.GetStringAsync("/categorias");
        foreach (var (nombre, _, _) in CategoriasPorDefecto.Lista)
            Assert.Contains(nombre, html);
    }

    [Fact]
    public async Task Crear_un_gasto_en_cuotas_lo_reparte_en_los_meses()
    {
        var (c, _) = await RegistrarAsync();
        var r = await PostFormAsync(c, "/gastos/nuevo", "/gastos/nuevo", new()
        {
            ["Descripcion"] = "Nueva Vision", ["Fecha"] = "2026-10-06", ["Moneda"] = "UYU", ["MontoCuota"] = "1830",
            ["EnCuotas"] = "true", ["CantidadCuotas"] = "6", ["CuotaActual"] = "5",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        Assert.Contains("Nueva Vision", await c.GetStringAsync("/gastos?mes=2026-06"));  // cuota 1
        Assert.Contains("Nueva Vision", await c.GetStringAsync("/gastos?mes=2026-11"));  // cuota 6
        Assert.DoesNotContain("Nueva Vision", await c.GetStringAsync("/gastos?mes=2026-05"));
        Assert.DoesNotContain("Nueva Vision", await c.GetStringAsync("/gastos?mes=2026-12"));
    }

    // ---------- Etapa 2 ----------

    [Fact]
    public async Task Un_usuario_no_puede_ver_ni_modificar_gastos_fijos_de_otro()
    {
        var (ana, _) = await RegistrarAsync();
        var crear = await PostFormAsync(ana, "/fijos/nuevo", "/fijos/nuevo", new()
        {
            ["Descripcion"] = "Fijo de Ana", ["Moneda"] = "UYU", ["Monto"] = "9000", ["DiaDelMes"] = "1",
            ["DesdeMes"] = "2026-01", ["TarjetaId"] = "",
        });
        Assert.Equal(HttpStatusCode.Redirect, crear.StatusCode);
        string lista = await ana.GetStringAsync("/fijos");
        Assert.Contains("Fijo de Ana", lista);
        string id = Regex.Match(lista, @"/fijos/(\d+)/editar").Groups[1].Value;

        var (beto, _) = await RegistrarAsync();
        Assert.DoesNotContain("Fijo de Ana", await beto.GetStringAsync("/fijos"));
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync($"/fijos/{id}/editar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(beto, "/fijos", $"/fijos/{id}/estado", new())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(beto, "/fijos", $"/fijos/{id}/eliminar", new())).StatusCode);

        // El fijo de Ana se cargó en sus meses y no en los de Beto.
        Assert.Contains("Fijo de Ana", await ana.GetStringAsync("/gastos?mes=2026-01"));
        Assert.DoesNotContain("Fijo de Ana", await beto.GetStringAsync("/gastos?mes=2026-01"));
    }

    [Fact]
    public async Task Guardar_y_cargar_otro_vuelve_al_formulario_con_la_misma_tarjeta_y_fecha()
    {
        var (c, _) = await RegistrarAsync();
        await PostFormAsync(c, "/tarjetas/nueva", "/tarjetas/nueva", new() { ["Nombre"] = "Visa", ["Tipo"] = "Credito", ["Color"] = "#1d4ed8", ["DiaCierre"] = "25" });
        string id = Regex.Match(await c.GetStringAsync("/tarjetas"), @"/tarjetas/(\d+)/editar").Groups[1].Value;

        var r = await PostFormAsync(c, "/gastos/nuevo", "/gastos/nuevo", new()
        {
            ["Descripcion"] = "Antel", ["Fecha"] = "2026-10-13", ["TarjetaId"] = id, ["Moneda"] = "UYU", ["MontoCuota"] = "65", ["otro"] = "1",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        string destino = r.Headers.Location!.ToString();
        Assert.Contains("/gastos/nuevo", destino);
        Assert.Contains($"tarjeta={id}", destino);
        Assert.Contains("fecha=2026-10-13", destino);

        // El formulario siguiente sugiere la descripción usada.
        Assert.Contains("<option value=\"Antel\">", await c.GetStringAsync(destino));
    }

    [Fact]
    public async Task No_se_borra_una_tarjeta_que_usa_un_gasto_fijo()
    {
        var (c, _) = await RegistrarAsync();
        await PostFormAsync(c, "/tarjetas/nueva", "/tarjetas/nueva", new() { ["Nombre"] = "Débito", ["Tipo"] = "Debito", ["Color"] = "#1d4ed8" });
        string id = Regex.Match(await c.GetStringAsync("/tarjetas"), @"/tarjetas/(\d+)/editar").Groups[1].Value;
        await PostFormAsync(c, "/fijos/nuevo", "/fijos/nuevo", new()
        {
            ["Descripcion"] = "Spotify", ["Moneda"] = "UYU", ["Monto"] = "300", ["DiaDelMes"] = "5", ["DesdeMes"] = "2099-01", ["TarjetaId"] = id,
        });

        var r = await PostFormAsync(c, "/tarjetas", $"/tarjetas/{id}/eliminar", new());
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode); // no 500
        Assert.Contains("Débito", await c.GetStringAsync("/tarjetas"));
    }
}
