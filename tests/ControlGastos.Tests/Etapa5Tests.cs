using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlGastos.Tests;

/// <summary>Reloj fijo para los tests.</summary>
public class TiempoFijo(DateTimeOffset ahora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ahora;
}

/// <summary>API simulada: responde lo que diga el test y cuenta las llamadas.</summary>
public class ApiFalsa(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public int Llamadas { get; private set; }
    public List<string> Urls { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Llamadas++;
        Urls.Add(request.RequestUri!.ToString());
        return Task.FromResult(responder(request));
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>Cotización automática y respaldo, sobre SQLite en memoria.</summary>
public class Etapa5Tests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private const string Ana = "user-ana";
    private const string Beto = "user-beto";
    private static readonly DateOnly Oct = new(2026, 10, 1);

    // Respuesta real de Frankfurter (agosto–octubre 2026, promedios mensuales).
    private const string RespuestaBcu =
        """[{"date":"2026-08-01","base":"USD","quote":"UYU","rate":40.255},{"date":"2026-09-01","base":"USD","quote":"UYU","rate":40.207},{"date":"2026-10-01","base":"USD","quote":"UYU","rate":40.183}]""";

    public Etapa5Tests()
    {
        _conn.Open();
        using var db = Contexto(null);
        db.Database.EnsureCreated();
        db.Users.AddRange(
            new AppUser { Id = Ana, UserName = "ana@test", Email = "ana@test" },
            new AppUser { Id = Beto, UserName = "beto@test", Email = "beto@test" });
        db.Categorias.AddRange(CategoriasPorDefecto.Crear(Ana));
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    private SqliteAppDbContext Contexto(string? userId) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options, new UsuarioFijo(userId));

    private static Reloj RelojEn(DateTime local) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:ZonaHoraria"] = "UTC" }).Build(),
            new TiempoFijo(new DateTimeOffset(local, TimeSpan.Zero)));

    private static CotizacionService Cotizaciones(AppDbContext db, ApiFalsa api, bool habilitado = true, DateTime? hoy = null) =>
        new(db,
            new HttpClient(api) { BaseAddress = new Uri("https://api.frankfurter.dev/") },
            RelojEn(hoy ?? new DateTime(2026, 10, 23, 12, 0, 0)),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Cotizaciones:Habilitado"] = habilitado.ToString() }).Build(),
            NullLogger<CotizacionService>.Instance);

    // ---------------- Cotización automática ----------------

    [Fact]
    public async Task Completa_los_meses_que_faltan_con_una_sola_consulta()
    {
        await using var db = Contexto(Ana);
        var api = new ApiFalsa(_ => ApiFalsa.Json(RespuestaBcu));
        int n = await Cotizaciones(db, api).AsegurarAsync(new DateOnly(2026, 8, 1), Oct);

        Assert.Equal(3, n);
        Assert.Equal(1, api.Llamadas);
        Assert.Contains("base=USD&quotes=UYU&providers=bcu&group=month", api.Urls[0]);
        var guardadas = await db.CotizacionesBcu.OrderBy(c => c.Mes).ToListAsync();
        Assert.Equal(new[] { 40.255m, 40.207m, 40.183m }, guardadas.Select(c => c.UsdEnPesos));
        Assert.True(guardadas[0].Definitiva);
        Assert.False(guardadas[2].Definitiva); // octubre todavía no cerró
    }

    [Fact]
    public async Task No_vuelve_a_consultar_lo_que_ya_tiene()
    {
        await using var db = Contexto(Ana);
        var api = new ApiFalsa(_ => ApiFalsa.Json(RespuestaBcu));
        var s = Cotizaciones(db, api);
        await s.AsegurarAsync(new DateOnly(2026, 8, 1), Oct);
        await s.AsegurarAsync(new DateOnly(2026, 8, 1), Oct);
        await s.AsegurarAsync(new DateOnly(2026, 9, 1), Oct);
        Assert.Equal(1, api.Llamadas); // el mes en curso ya se consultó hoy

        await s.AsegurarAsync(new DateOnly(2026, 8, 1), Oct, forzar: true);
        Assert.Equal(2, api.Llamadas);
    }

    [Fact]
    public async Task El_mes_en_curso_se_actualiza_al_dia_siguiente_y_se_cierra_al_terminar()
    {
        await using var db = Contexto(Ana);
        var api = new ApiFalsa(_ => ApiFalsa.Json(RespuestaBcu));
        await Cotizaciones(db, api).AsegurarAsync(Oct, Oct);
        // Simula que la consulta fue ayer.
        (await db.CotizacionesBcu.SingleAsync()).ActualizadoEn = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        await Cotizaciones(db, api).AsegurarAsync(Oct, Oct);
        Assert.Equal(2, api.Llamadas);

        // Ya en noviembre, octubre pasa a definitiva.
        await Cotizaciones(db, api, hoy: new DateTime(2026, 11, 3)).AsegurarAsync(Oct, Oct);
        Assert.True((await db.CotizacionesBcu.SingleAsync()).Definitiva);
    }

    [Fact]
    public async Task Si_la_API_falla_sigue_con_lo_guardado()
    {
        await using var db = Contexto(Ana);
        db.CotizacionesBcu.Add(new CotizacionBcu { Mes = new DateOnly(2026, 9, 1), UsdEnPesos = 40.2m, Definitiva = true });
        await db.SaveChangesAsync();

        var caida = new ApiFalsa(_ => throw new HttpRequestException("sin conexión"));
        Assert.Equal(0, await Cotizaciones(db, caida).AsegurarAsync(new DateOnly(2026, 9, 1), Oct));
        var basura = new ApiFalsa(_ => ApiFalsa.Json("<html>error</html>"));
        Assert.Equal(0, await Cotizaciones(db, basura).AsegurarAsync(new DateOnly(2026, 9, 1), Oct));

        Assert.Equal(40.2m, (await db.CotizacionesBcu.SingleAsync()).UsdEnPesos);
    }

    [Fact]
    public async Task Descarta_valores_absurdos_y_no_consulta_si_esta_deshabilitada()
    {
        await using var db = Contexto(Ana);
        var rara = new ApiFalsa(_ => ApiFalsa.Json("""[{"date":"2026-10-01","rate":-5},{"date":"2026-09-01","rate":0}]"""));
        Assert.Equal(0, await Cotizaciones(db, rara).AsegurarAsync(new DateOnly(2026, 9, 1), Oct));
        Assert.Empty(await db.CotizacionesBcu.ToListAsync());

        var api = new ApiFalsa(_ => ApiFalsa.Json(RespuestaBcu));
        await Cotizaciones(db, api, habilitado: false).AsegurarAsync(new DateOnly(2026, 8, 1), Oct);
        Assert.Equal(0, api.Llamadas);
    }

    [Fact]
    public void El_ajuste_manual_manda_sobre_el_BCU()
    {
        var t = new TablaTiposCambio(
            new[] { new TipoCambio { Mes = new DateOnly(2026, 9, 1), UsdEnPesos = 41m } },
            new[] { new CotizacionBcu { Mes = new DateOnly(2026, 8, 1), UsdEnPesos = 40.255m }, new CotizacionBcu { Mes = new DateOnly(2026, 9, 1), UsdEnPesos = 40.207m } });
        Assert.Equal(40.255m, t.Para(new DateOnly(2026, 8, 1)));
        Assert.Equal(41m, t.Para(new DateOnly(2026, 9, 1)));
        Assert.True(t.EsManual(new DateOnly(2026, 9, 1)));
        Assert.Equal(41m, t.Para(Oct)); // sin dato: el último anterior
    }

    [Fact]
    public async Task Todo_en_pesos_usa_el_BCU_sin_cargar_nada_a_mano()
    {
        await using var db = Contexto(Ana);
        var gastos = new GastoService(db);
        await gastos.CrearAsync(new GastoInput("Netflix", null, new DateOnly(2026, 10, 21), null, null, Moneda.USD, 10m, 1, 1, null));
        await gastos.CrearAsync(new GastoInput("Antel", null, new DateOnly(2026, 10, 13), null, null, Moneda.UYU, 65m, 1, 1, null));
        var api = new ApiFalsa(_ => ApiFalsa.Json(RespuestaBcu));

        var r = await new ReporteService(db, gastos, Cotizaciones(db, api)).GenerarAsync(Oct, Oct, Oct, VistaMoneda.Pesificado);
        Assert.Empty(r.MesesSinTipoCambio);
        Assert.Equal(65m + 401.83m, r.Total);
    }

    // ---------------- Respaldo ----------------

    private async Task CargarDatosDeAnaAsync()
    {
        await using var db = Contexto(Ana);
        var visa = new Tarjeta { Nombre = "Visa", Tipo = TipoTarjeta.Credito, DiaCierre = 25, Color = "#1d4ed8" };
        db.Tarjetas.Add(visa);
        await db.SaveChangesAsync();
        var cats = await db.Categorias.ToDictionaryAsync(c => c.Nombre, c => c.Id);
        var g = new GastoService(db);
        await g.CrearAsync(new GastoInput("Nueva Visión", "NUEVA VISION", new DateOnly(2026, 10, 6), visa.Id, cats["Salud"], Moneda.UYU, 1830m, 6, 5, null));
        await g.CrearAsync(new GastoInput("Netflix", "NETFLIX.COM", new DateOnly(2026, 10, 21), visa.Id, cats["Suscripciones"], Moneda.USD, 5.17m, 1, 1, "nota"));
        await new RecurrenteService(db).CrearAsync(new RecurrenteInput("Facultad", null, cats["Educación"], Moneda.UYU, 9000m, 1, new DateOnly(2026, 9, 1), null, null), Oct);
        db.TiposCambio.Add(new TipoCambio { Mes = Oct, UsdEnPesos = 40.5m });
        await new PresupuestoService(db).GuardarAsync(cats["Salud"], Moneda.UYU, 3000m);
        await db.SaveChangesAsync();
    }

    private async Task<byte[]> ExportarDeAsync(string user)
    {
        await using var db = Contexto(user);
        return await new RespaldoService(db).ExportarAsync(new DateTime(2026, 10, 23));
    }

    [Fact]
    public async Task Exportar_y_restaurar_en_otra_cuenta_reproduce_todo()
    {
        await CargarDatosDeAnaAsync();
        var json = await ExportarDeAsync(Ana);

        await using (var dbBeto = Contexto(Beto))
        {
            dbBeto.Tarjetas.Add(new Tarjeta { Nombre = "Vieja de Beto", Tipo = TipoTarjeta.Debito });
            await dbBeto.SaveChangesAsync();
            var res = await new RespaldoService(dbBeto).RestaurarAsync(RespaldoService.Leer(new MemoryStream(json)));
            // Nueva Visión, Netflix y la facultad de septiembre y octubre (generadas por el gasto fijo).
            Assert.Equal(new ResultadoRestauracion(Tarjetas: 1, Categorias: 14, GastosFijos: 1, Gastos: 4, TiposCambio: 1, Presupuestos: 1), res);
        }
    }

    [Fact]
    public async Task Restaurar_reemplaza_los_datos_sin_tocar_los_de_otros()
    {
        await CargarDatosDeAnaAsync();
        var json = await ExportarDeAsync(Ana);
        await using (var dbBeto = Contexto(Beto))
        {
            dbBeto.Tarjetas.Add(new Tarjeta { Nombre = "Vieja de Beto", Tipo = TipoTarjeta.Debito });
            await dbBeto.SaveChangesAsync();
            await new RespaldoService(dbBeto).RestaurarAsync(RespaldoService.Leer(new MemoryStream(json)));
        }

        await using var beto = Contexto(Beto);
        await using var ana = Contexto(Ana);
        Assert.DoesNotContain(await beto.Tarjetas.ToListAsync(), t => t.Nombre == "Vieja de Beto");

        // Mismos totales de octubre en las dos cuentas.
        var rAna = await new GastoService(ana).ResumenAsync(Oct);
        var rBeto = await new GastoService(beto).ResumenAsync(Oct);
        Assert.Equal(rAna.Total(Moneda.UYU), rBeto.Total(Moneda.UYU));
        Assert.Equal(rAna.Total(Moneda.USD), rBeto.Total(Moneda.USD));
        Assert.Equal(new DateOnly(2026, 6, 1), (await beto.Gastos.SingleAsync(g => g.Comercio == "NUEVA VISION")).PrimerPeriodo);
        Assert.Equal(6, await beto.Cuotas.CountAsync(c => c.Gasto.Comercio == "NUEVA VISION"));
        Assert.Equal(25, (await beto.Tarjetas.SingleAsync()).DiaCierre);
        Assert.Single(await beto.Presupuestos.ToListAsync());
        Assert.Equal(40.5m, (await beto.TiposCambio.SingleAsync()).UsdEnPesos);

        // El fijo conserva hasta dónde se generó: no duplica meses.
        Assert.Equal(0, await new RecurrenteService(beto).GenerarPendientesAsync(Oct));
        Assert.Equal(await ana.Gastos.CountAsync(), await beto.Gastos.CountAsync());

        // Los datos de Ana siguen intactos y son suyos.
        Assert.All(await ana.Gastos.ToListAsync(), g => Assert.Equal(Ana, g.UserId));
        Assert.All(await beto.Gastos.ToListAsync(), g => Assert.Equal(Beto, g.UserId));
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"app":"Otra app","version":1}""")]
    [InlineData("""{"app":"Mis·Gastos","version":99,"exportadoEn":"2026-10-23T00:00:00","tarjetas":[],"categorias":[],"gastosFijos":[],"gastos":[],"tiposCambio":[],"presupuestos":[]}""")]
    [InlineData("""{"app":"Mis·Gastos","version":1,"exportadoEn":"2026-10-23T00:00:00","tarjetas":[],"categorias":[],"gastosFijos":[],"gastos":[],"tiposCambio":[],"presupuestos":[],"campoRaro":1}""")]
    [InlineData("""{"app":"Mis·Gastos","version":1,"exportadoEn":"2026-10-23T00:00:00","tarjetas":[],"categorias":[],"gastosFijos":[],"gastos":[{"descripcion":"X","comercio":null,"fecha":"2026-10-01","tarjetaId":7,"categoriaId":null,"moneda":"UYU","montoCuota":10,"cantidadCuotas":1,"primerPeriodo":"2026-10-01","notas":null,"recurrenteId":null,"creadoEn":"2026-10-01T00:00:00"}],"tiposCambio":[],"presupuestos":[]}""")]
    [InlineData("""{"app":"Mis·Gastos","version":1,"exportadoEn":"2026-10-23T00:00:00","tarjetas":[{"id":1,"nombre":"V","tipo":"Credito","color":"red; background:url(x)","activa":true,"diaCierre":null}],"categorias":[],"gastosFijos":[],"gastos":[],"tiposCambio":[],"presupuestos":[]}""")]
    [InlineData("""{"app":"Mis·Gastos","version":1,"exportadoEn":"2026-10-23T00:00:00","tarjetas":[],"categorias":[],"gastosFijos":[],"gastos":[{"descripcion":"X","comercio":null,"fecha":"2026-10-01","tarjetaId":null,"categoriaId":null,"moneda":"UYU","montoCuota":-10,"cantidadCuotas":1,"primerPeriodo":"2026-10-01","notas":null,"recurrenteId":null,"creadoEn":"2026-10-01T00:00:00"}],"tiposCambio":[],"presupuestos":[]}""")]
    public async Task Un_archivo_invalido_se_rechaza_sin_tocar_nada(string contenido)
    {
        await CargarDatosDeAnaAsync();
        int antes;
        await using (var db = Contexto(Ana)) antes = await db.Gastos.CountAsync();

        Assert.Throws<ValidacionException>(() => RespaldoService.Leer(new MemoryStream(Encoding.UTF8.GetBytes(contenido))));

        await using var db2 = Contexto(Ana);
        Assert.Equal(antes, await db2.Gastos.CountAsync());
    }

    [Fact]
    public async Task Restaurar_sin_usuario_no_hace_nada()
    {
        await CargarDatosDeAnaAsync();
        var json = await ExportarDeAsync(Ana);
        await using var db = Contexto(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RespaldoService(db).RestaurarAsync(RespaldoService.Leer(new MemoryStream(json))));
    }

    // ---------------- Comandos de administración ----------------

    [Fact]
    public void La_contrasena_temporal_es_aleatoria_y_cumple_la_politica()
    {
        var a = ComandosAdmin.GenerarTemporal();
        var b = ComandosAdmin.GenerarTemporal();
        Assert.Matches("^[a-z2-9]{4}-[a-z2-9]{4}-[a-z2-9]{4}-[a-z2-9]{4}$", a);
        Assert.NotEqual(a, b);
        Assert.Contains(a, char.IsDigit);
        Assert.Contains(a, char.IsLower);
    }
}

/// <summary>Respaldo, comandos de administración y app instalable, por HTTP.</summary>
public class Etapa5HttpTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

    private static async Task<string> TokenAsync(HttpClient c, string url) => TokenRegex.Match(await c.GetStringAsync(url)).Groups[1].Value;

    private async Task<(HttpClient Cliente, string Email)> RegistrarAsync(string password = "clave1234")
    {
        var c = factory.Cliente();
        string email = $"u{Guid.NewGuid():N}@test.local";
        var r = await c.PostAsync("/cuenta/registro", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(c, "/cuenta/registro"),
            ["Nombre"] = "Test", ["Email"] = email, ["Password"] = password, ["ConfirmarPassword"] = password,
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return (c, email);
    }

    private static async Task<HttpResponseMessage> CrearGastoAsync(HttpClient c, string descripcion) =>
        await c.PostAsync("/gastos/nuevo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(c, "/gastos/nuevo"),
            ["Descripcion"] = descripcion, ["Fecha"] = "2026-10-10", ["Moneda"] = "UYU", ["MontoCuota"] = "100",
        }));

    private static async Task<HttpResponseMessage> RestaurarAsync(HttpClient c, byte[] json, string password, bool confirmo = true)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(await TokenAsync(c, "/cuenta/perfil")), "__RequestVerificationToken" },
            { new StringContent(password), "passwordActual" },
            { new StringContent(confirmo ? "true" : "false"), "confirmo" },
            { new ByteArrayContent(json), "archivo", "respaldo.json" },
        };
        return await c.PostAsync("/cuenta/respaldo/restaurar", form);
    }

    [Fact]
    public async Task El_respaldo_es_privado_y_contiene_solo_mis_datos()
    {
        Assert.Equal(HttpStatusCode.Redirect, (await factory.Cliente().GetAsync("/cuenta/respaldo")).StatusCode);

        var (ana, _) = await RegistrarAsync();
        await CrearGastoAsync(ana, "Gasto de Ana");
        var (beto, _) = await RegistrarAsync();
        await CrearGastoAsync(beto, "Gasto de Beto");

        var r = await ana.GetAsync("/cuenta/respaldo");
        Assert.Equal("application/json", r.Content.Headers.ContentType!.MediaType);
        Assert.Contains("attachment", r.Content.Headers.ContentDisposition!.DispositionType);
        string json = await r.Content.ReadAsStringAsync();
        Assert.Contains("Gasto de Ana", json);
        Assert.DoesNotContain("Gasto de Beto", json);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restaurar_pide_contrasena_y_confirmacion()
    {
        var (c, _) = await RegistrarAsync();
        await CrearGastoAsync(c, "Gasto original");
        var respaldo = await (await c.GetAsync("/cuenta/respaldo")).Content.ReadAsByteArrayAsync();
        await CrearGastoAsync(c, "Gasto posterior");

        await RestaurarAsync(c, respaldo, "incorrecta1");
        await RestaurarAsync(c, respaldo, "clave1234", confirmo: false);
        Assert.Contains("Gasto posterior", await c.GetStringAsync("/gastos?mes=2026-10")); // nada cambió

        var ok = await RestaurarAsync(c, respaldo, "clave1234");
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        string mes = await c.GetStringAsync("/gastos?mes=2026-10");
        Assert.Contains("Gasto original", mes);
        Assert.DoesNotContain("Gasto posterior", mes);
    }

    [Fact]
    public async Task Reset_password_por_consola_permite_entrar_con_la_temporal()
    {
        var (_, email) = await RegistrarAsync("ClaveOlvidada1");
        var salida = new StringWriter();
        int codigo = await ComandosAdmin.EjecutarAsync(factory.Services, new[] { "admin", "reset-password", email }, salida);
        Assert.Equal(0, codigo);
        string temporal = Regex.Match(salida.ToString(), @"Contraseña temporal de [^:]+: (\S+)").Groups[1].Value;
        Assert.NotEmpty(temporal);

        async Task<HttpResponseMessage> Login(string pass)
        {
            var c = factory.Cliente();
            return await c.PostAsync("/cuenta/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(c, "/cuenta/login"), ["Email"] = email, ["Password"] = pass,
            }));
        }
        Assert.Equal(HttpStatusCode.OK, (await Login("ClaveOlvidada1")).StatusCode);   // la vieja ya no sirve
        Assert.Equal(HttpStatusCode.Redirect, (await Login(temporal)).StatusCode);    // la temporal sí

        var lista = new StringWriter();
        await ComandosAdmin.EjecutarAsync(factory.Services, new[] { "admin", "usuarios" }, lista);
        Assert.Contains(email, lista.ToString());
        Assert.Equal(1, await ComandosAdmin.EjecutarAsync(factory.Services, new[] { "admin", "reset-password", "nadie@test.local" }, new StringWriter()));
    }

    [Fact]
    public async Task Desbloquear_por_consola_quita_el_bloqueo()
    {
        var (_, email) = await RegistrarAsync();
        var c = factory.Cliente();
        for (int i = 0; i < 5; i++)
            await c.PostAsync("/cuenta/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(c, "/cuenta/login"), ["Email"] = email, ["Password"] = "mala1234",
            }));
        await ComandosAdmin.EjecutarAsync(factory.Services, new[] { "admin", "desbloquear", email }, new StringWriter());
        var r = await c.PostAsync("/cuenta/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(c, "/cuenta/login"), ["Email"] = email, ["Password"] = "clave1234",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
    }

    [Theory]
    [InlineData("/manifest.webmanifest", "application/manifest+json")]
    [InlineData("/sw.js", "text/javascript")]
    [InlineData("/offline.html", "text/html")]
    [InlineData("/icons/icon-192.png", "image/png")]
    [InlineData("/icons/maskable-512.png", "image/png")]
    public async Task Los_archivos_de_la_app_instalable_son_publicos(string url, string tipo)
    {
        var r = await factory.Cliente().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(tipo, r.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Las_paginas_enlazan_el_manifest()
    {
        string html = await factory.Cliente().GetStringAsync("/cuenta/login");
        Assert.Contains("<link rel=\"manifest\" href=\"/manifest.webmanifest\"", html);
        Assert.Contains("apple-touch-icon", html);
    }

    [Fact]
    public async Task El_service_worker_no_guarda_paginas_con_datos()
    {
        string sw = await factory.Cliente().GetStringAsync("/sw.js");
        // Las páginas van siempre a la red; solo se guardan en caché css, js, lib, íconos y fuentes.
        Assert.Contains("e.respondWith(fetch(req).catch(() => caches.match(\"/offline.html\")))", sw);
        Assert.Contains(@"const ESTATICOS = /^\/(css|js|lib|icons|fonts)\//;", sw);
        Assert.Single(Regex.Matches(sw, @"cache\.put\("));
    }
}
