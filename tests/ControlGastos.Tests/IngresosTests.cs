using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Services;
using ControlGastos.Web.ViewModels;

namespace ControlGastos.Tests;

/// <summary>Ingresos, "cuánto te queda" y la gráfica circular por categoría.</summary>
public class IngresosTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient c, string formUrl, string postUrl, Dictionary<string, string> datos)
    {
        datos["__RequestVerificationToken"] = TokenRegex.Match(await c.GetStringAsync(formUrl)).Groups[1].Value;
        return await c.PostAsync(postUrl, new FormUrlEncodedContent(datos));
    }

    private async Task<HttpClient> RegistrarAsync()
    {
        var c = factory.Cliente();
        var r = await PostFormAsync(c, "/cuenta/registro", "/cuenta/registro", new()
        {
            ["Nombre"] = "Ingresos", ["Email"] = $"i{Guid.NewGuid():N}@test.local", ["Password"] = "clave1234", ["ConfirmarPassword"] = "clave1234",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return c;
    }

    private static Task<HttpResponseMessage> CrearIngresoAsync(HttpClient c, string descripcion, decimal monto, string moneda = "UYU", string tipo = "Sueldo", string fecha = "2026-10-01") =>
        PostFormAsync(c, "/ingresos/nuevo", "/ingresos/nuevo", new()
        {
            ["Descripcion"] = descripcion, ["Monto"] = monto.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Moneda"] = moneda, ["Tipo"] = tipo, ["Fecha"] = fecha,
        });

    private static Task<HttpResponseMessage> CrearGastoAsync(HttpClient c, string descripcion, decimal monto, string categoria = "") =>
        PostFormAsync(c, "/gastos/nuevo", "/gastos/nuevo", new()
        {
            ["Descripcion"] = descripcion, ["Fecha"] = "2026-10-10", ["Moneda"] = "UYU",
            ["MontoCuota"] = monto.ToString(System.Globalization.CultureInfo.InvariantCulture), ["CategoriaId"] = categoria,
        });

    // ---------- Ingresos ----------

    [Fact]
    public async Task Cargar_editar_y_borrar_un_ingreso()
    {
        var c = await RegistrarAsync();
        var r = await CrearIngresoAsync(c, "Sueldo de octubre", 40000m);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/ingresos?mes=2026-10", r.Headers.Location!.OriginalString);

        string lista = await c.GetStringAsync("/ingresos?mes=2026-10");
        Assert.Contains("Sueldo de octubre", lista);
        Assert.Contains("+ $ 40.000,00", lista);
        string id = Regex.Match(lista, "/ingresos/(\\d+)/editar").Groups[1].Value;

        await PostFormAsync(c, $"/ingresos/{id}/editar", $"/ingresos/{id}/editar", new()
        {
            ["Descripcion"] = "Venta de la bici", ["Monto"] = "5000", ["Moneda"] = "UYU", ["Tipo"] = "Venta", ["Fecha"] = "2026-10-15",
        });
        lista = await c.GetStringAsync("/ingresos?mes=2026-10");
        Assert.Contains("Venta de la bici", lista);
        Assert.Contains("Venta", lista);
        Assert.DoesNotContain("Sueldo de octubre", lista);

        await PostFormAsync(c, "/ingresos?mes=2026-10", $"/ingresos/{id}/eliminar", new());
        Assert.DoesNotContain("Venta de la bici", await c.GetStringAsync("/ingresos?mes=2026-10"));
    }

    [Fact]
    public async Task Un_ingreso_invalido_no_se_guarda()
    {
        var c = await RegistrarAsync();
        var r = await CrearIngresoAsync(c, "", -5m);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // vuelve al formulario con los errores
        string html = await r.Content.ReadAsStringAsync();
        Assert.Contains("Ingresa una descripción.", html);
        Assert.Contains("El monto debe ser mayor a cero.", html);
    }

    [Fact]
    public async Task Los_ingresos_de_otro_usuario_no_se_ven_ni_se_tocan()
    {
        var ana = await RegistrarAsync();
        await CrearIngresoAsync(ana, "Sueldo de Ana", 40000m);
        string id = Regex.Match(await ana.GetStringAsync("/ingresos?mes=2026-10"), "/ingresos/(\\d+)/editar").Groups[1].Value;

        var beto = await RegistrarAsync();
        Assert.DoesNotContain("Sueldo de Ana", await beto.GetStringAsync("/ingresos?mes=2026-10"));
        Assert.Equal(HttpStatusCode.NotFound, (await beto.GetAsync($"/ingresos/{id}/editar")).StatusCode);
        var editar = await PostFormAsync(beto, "/ingresos/nuevo", $"/ingresos/{id}/editar", new()
        {
            ["Descripcion"] = "Robado", ["Monto"] = "1", ["Moneda"] = "UYU", ["Tipo"] = "Otro", ["Fecha"] = "2026-10-01",
        });
        Assert.Equal(HttpStatusCode.NotFound, editar.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(beto, "/ingresos/nuevo", $"/ingresos/{id}/eliminar", new())).StatusCode);

        Assert.Contains("Sueldo de Ana", await ana.GetStringAsync("/ingresos?mes=2026-10"));
    }

    [Theory]
    [InlineData("/ingresos")]
    [InlineData("/ingresos/nuevo")]
    public async Task Ingresos_es_una_ruta_privada(string url)
    {
        var r = await factory.Cliente().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/cuenta/login", r.Headers.Location!.OriginalString);
    }

    // ---------- Cuánto te queda ----------

    [Fact]
    public async Task El_inicio_muestra_cuanto_te_queda()
    {
        var c = await RegistrarAsync();
        string sinIngresos = await c.GetStringAsync("/panel?mes=2026-10");
        Assert.Contains("Cuánto te queda", sinIngresos);
        Assert.Contains("Cargar un ingreso", sinIngresos);

        await CrearIngresoAsync(c, "Sueldo", 40000m);
        await CrearGastoAsync(c, "Tarjeta", 20000m);
        string html = await c.GetStringAsync("/panel?mes=2026-10");
        string balance = Regex.Match(html, "<section class=\"card-x balance[\\s\\S]*?</section>").Value;
        Assert.Contains("$ 40.000,00", balance);                              // ingresos
        Assert.Contains("$ 20.000,00", balance);                              // gastos
        Assert.Contains("Te queda", balance);
        Assert.Contains("20.000<span class=\"cents\">,00</span>", balance);   // lo que queda
        Assert.Contains("Ya se fue el 50 % de lo que entró.", balance);
    }

    [Fact]
    public async Task Si_gastaste_mas_de_lo_que_entro_lo_dice()
    {
        var c = await RegistrarAsync();
        await CrearIngresoAsync(c, "Venta", 1000m, tipo: "Venta");
        await CrearGastoAsync(c, "Compra grande", 1500m);
        string html = await c.GetStringAsync("/ingresos?mes=2026-10");
        Assert.Contains("Te pasaste por", html);
        Assert.Contains("Gastaste $ 500,00 más de lo que entró.", html);
    }

    [Fact]
    public async Task Sin_ingresos_en_dolares_no_avisa_que_te_pasaste()
    {
        var c = await RegistrarAsync();
        await CrearIngresoAsync(c, "Sueldo", 40000m);
        await PostFormAsync(c, "/gastos/nuevo", "/gastos/nuevo", new()
        {
            ["Descripcion"] = "Netflix", ["Fecha"] = "2026-10-10", ["Moneda"] = "USD", ["MontoCuota"] = "10",
        });
        string balance = Regex.Match(await c.GetStringAsync("/panel?mes=2026-10"), "<section class=\"card-x balance[\\s\\S]*?</section>").Value;
        Assert.Contains("En dólares", balance);
        Assert.Contains("No cargaste ingresos en dólares este mes.", balance);
        Assert.DoesNotContain("Te pasaste", balance);
    }

    [Fact]
    public void El_balance_no_mezcla_pesos_y_dolares()
    {
        var ingresos = new[]
        {
            new Ingreso { Moneda = Moneda.UYU, Monto = 40000m },
            new Ingreso { Moneda = Moneda.USD, Monto = 100m },
        };
        var gastos = new ResumenMes(new DateOnly(2026, 10, 1), new(), new() { [Moneda.UYU] = 25000m, [Moneda.USD] = 30m }, new(), new(), new());
        var b = IngresoService.Calcular(ingresos, gastos);
        Assert.Equal(15000m, b.Pesos.Queda);
        Assert.Equal(70m, b.Dolares.Queda);
        Assert.Equal(62.5m, b.Pesos.PorcentajeGastado);
        Assert.Equal(2, b.Visibles.Count());
    }

    // ---------- Respaldo ----------

    [Fact]
    public async Task El_respaldo_incluye_los_ingresos_y_los_restaura()
    {
        var c = await RegistrarAsync();
        await CrearIngresoAsync(c, "Sueldo respaldado", 40000m);
        var respaldo = await (await c.GetAsync("/cuenta/respaldo")).Content.ReadAsByteArrayAsync();
        string json = Encoding.UTF8.GetString(respaldo);
        Assert.Contains("\"version\": 2", json);
        Assert.Contains("Sueldo respaldado", json);

        await CrearIngresoAsync(c, "Ingreso posterior", 10m);
        var form = new MultipartFormDataContent
        {
            { new StringContent(TokenRegex.Match(await c.GetStringAsync("/cuenta/perfil")).Groups[1].Value), "__RequestVerificationToken" },
            { new StringContent("clave1234"), "passwordActual" },
            { new StringContent("true"), "confirmo" },
            { new ByteArrayContent(respaldo), "archivo", "respaldo.json" },
        };
        Assert.Equal(HttpStatusCode.Redirect, (await c.PostAsync("/cuenta/respaldo/restaurar", form)).StatusCode);
        string lista = await c.GetStringAsync("/ingresos?mes=2026-10");
        Assert.Contains("Sueldo respaldado", lista);
        Assert.DoesNotContain("Ingreso posterior", lista);
    }

    [Fact]
    public void Un_respaldo_viejo_sin_ingresos_se_sigue_pudiendo_leer()
    {
        const string v1 = """{"app":"Mis·Gastos","version":1,"exportadoEn":"2026-10-23T00:00:00","tarjetas":[],"categorias":[],"gastosFijos":[],"gastos":[],"tiposCambio":[],"presupuestos":[]}""";
        var r = RespaldoService.Leer(new MemoryStream(Encoding.UTF8.GetBytes(v1)));
        Assert.Empty(r.Ingresos!);

        // En la versión 2 la sección es obligatoria, y un ingreso inválido se rechaza.
        const string v2SinIngresos = """{"app":"Mis·Gastos","version":2,"exportadoEn":"2026-10-23T00:00:00","tarjetas":[],"categorias":[],"gastosFijos":[],"gastos":[],"tiposCambio":[],"presupuestos":[]}""";
        Assert.Throws<ValidacionException>(() => RespaldoService.Leer(new MemoryStream(Encoding.UTF8.GetBytes(v2SinIngresos))));
        const string v2Invalido = """{"app":"Mis·Gastos","version":2,"exportadoEn":"2026-10-23T00:00:00","tarjetas":[],"categorias":[],"gastosFijos":[],"gastos":[],"tiposCambio":[],"presupuestos":[],"ingresos":[{"descripcion":"X","tipo":"Sueldo","fecha":"2026-10-01","moneda":"UYU","monto":-1,"notas":null,"creadoEn":"2026-10-01T00:00:00"}]}""";
        Assert.Throws<ValidacionException>(() => RespaldoService.Leer(new MemoryStream(Encoding.UTF8.GetBytes(v2Invalido))));
    }

    // ---------- Gráfica circular ----------

    [Fact]
    public void La_dona_tiene_como_mucho_seis_porciones_y_junta_el_resto()
    {
        var items = Enumerable.Range(1, 9).Select(i => new PorcionDona($"Cat {i}", "#16a34a", i * 100m)).Append(new PorcionDona("Vacía", "#000000", 0m));
        var d = DonaVm.Crear(items, Moneda.UYU);
        Assert.Equal(6, d.Porciones.Count);
        Assert.Equal("Cat 9", d.Porciones[0].Nombre);                 // de mayor a menor
        Assert.Equal("Otras (4)", d.Porciones[^1].Nombre);            // Cat 1 a Cat 4
        Assert.Equal(1000m, d.Porciones[^1].Total);
        Assert.Equal(4500m, d.Total);
        Assert.Equal(9, d.Categorias);                                  // la vacía no cuenta
    }

    [Fact]
    public async Task Gastos_del_mes_muestra_la_dona_con_porcentajes_y_nombres()
    {
        var c = await RegistrarAsync();
        string form = await c.GetStringAsync("/gastos/nuevo");
        string salud = Regex.Match(form, "<option value=\"(\\d+)\">Salud</option>").Groups[1].Value;
        string hogar = Regex.Match(form, "<option value=\"(\\d+)\">Hogar</option>").Groups[1].Value;
        await CrearGastoAsync(c, "Farmacia", 750m, salud);
        await CrearGastoAsync(c, "Lámpara", 250m, hogar);

        string html = await c.GetStringAsync("/gastos?mes=2026-10");
        string dona = Regex.Match(html, "<div class=\"dona\" data-dona>[\\s\\S]*?</ul>").Value;
        Assert.Contains("data-nombre=\"Salud\" data-pct=\"75\"", dona);   // el nombre viaja para el tooltip
        Assert.Contains("data-nombre=\"Hogar\" data-pct=\"25\"", dona);
        Assert.Contains("<span class=\"dona-nombre\">Salud</span>", dona);
        Assert.Contains("<span class=\"dona-pct\">75 %</span>", dona);
        Assert.Contains("aria-hidden=\"true\"", dona);                    // el SVG es decorativo; la leyenda es la versión accesible
    }

    [Fact]
    public async Task Reportes_muestra_la_dona_por_categoria()
    {
        var c = await RegistrarAsync();
        await CrearGastoAsync(c, "Algo", 100m);
        string html = await c.GetStringAsync("/reportes?desde=2026-10&hasta=2026-10");
        Assert.Contains("data-dona", html);
        Assert.Contains("Sin categoría", html);
    }
}
