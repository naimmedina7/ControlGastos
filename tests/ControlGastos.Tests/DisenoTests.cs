using System.Net;
using System.Text.RegularExpressions;

namespace ControlGastos.Tests;

/// <summary>Rediseño visual: lo que se puede comprobar en el HTML y en los estáticos.</summary>
public class DisenoTests(AppFactory factory) : IClassFixture<AppFactory>
{
    // ---------- Fase 1 ----------

    [Fact]
    public async Task La_pantalla_de_acceso_tiene_vista_previa_al_compartir_y_titulo_sin_repetir()
    {
        string html = await factory.Cliente().GetStringAsync("/cuenta/login");
        Assert.Contains("<title>Iniciar sesión · Mis·Gastos</title>", html);
        Assert.Contains("<meta property=\"og:image\" content=\"https://localhost/icons/og-imagen.png\"", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\"", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", html); // app privada: no se indexa
        var og = await factory.Cliente().GetAsync("/icons/og-imagen.png");
        Assert.Equal(HttpStatusCode.OK, og.StatusCode);
        Assert.Equal("image/png", og.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task La_pantalla_de_acceso_no_muestra_el_stack_ni_datos_reales()
    {
        string html = await factory.Cliente().GetStringAsync("/cuenta/login");
        Assert.DoesNotContain("ASP.NET", html);
        Assert.Contains("Hecho por Fourvexa", html);
        foreach (var real in new[] { "Cueva", "Tiendamia", "MERCADOLIBRE", "1012" })
            Assert.DoesNotContain(real, html);
    }

    [Fact]
    public async Task El_service_worker_usa_la_version_exacta_de_cada_estatico()
    {
        string sw = await factory.Cliente().GetStringAsync("/sw.js");
        // Primero la URL exacta (con ?v=…); la versión "cualquiera" solo sin conexión.
        int exacta = sw.IndexOf("await cache.match(req);", StringComparison.Ordinal);
        int cualquiera = sw.IndexOf("cache.match(req, { ignoreSearch: true })", StringComparison.Ordinal);
        Assert.True(exacta > 0 && cualquiera > exacta);
        Assert.Contains("catch (err)", sw[exacta..]);
    }

    [Fact]
    public async Task Los_estilos_no_tienen_texto_menor_a_12px()
    {
        string css = await factory.Cliente().GetStringAsync("/css/site.css");
        // Con base de 15 px, .8rem = 12 px. No debe quedar ningún font-size en rem por debajo de eso.
        var menores = Regex.Matches(css, @"font-size:\s*(\.\d+)rem").Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Where(v => v < .8).ToList();
        Assert.Empty(menores);
        Assert.DoesNotContain("html { font-size: 14.5px; }", css);
    }

    // ---------- Fase 2 ----------

    [Fact]
    public async Task La_tipografia_de_marca_esta_alojada_en_el_sitio_y_se_precarga()
    {
        string html = await factory.Cliente().GetStringAsync("/cuenta/login");
        Assert.Contains("<link rel=\"preload\" href=\"/fonts/bricolage-grotesque-latin.woff2\" as=\"font\" type=\"font/woff2\" crossorigin", html);
        var fuente = await factory.Cliente().GetAsync("/fonts/bricolage-grotesque-latin.woff2");
        Assert.Equal(HttpStatusCode.OK, fuente.StatusCode);
        string css = await factory.Cliente().GetStringAsync("/css/site.css");
        Assert.Contains("font-variant-numeric: tabular-nums", css); // cifras alineadas en tablas y totales
    }

    [Fact]
    public async Task Los_tokens_de_color_son_los_del_sistema_nuevo()
    {
        string css = await factory.Cliente().GetStringAsync("/css/site.css");
        string root = css[css.IndexOf(":root {", StringComparison.Ordinal)..];
        root = root[..root.IndexOf('}')];
        foreach (var token in new[] { "--bg: #f6f4ef", "--ink: #0b1f1a", "--accent: #047857", "--mint: #6ee7b7", "--amber: #fbbf24", "--amber-ink: #b45309", "--r-ctrl: 12px", "--r-card: 20px" })
            Assert.Contains(token, root);
        // La paleta de los gráficos no se toca: está validada para daltonismo.
        foreach (var serie in new[] { "--series-1: #2a78d6", "--series-2: #eb6834", "--series-3: #1baf7a" })
            Assert.Contains(serie, root);
        Assert.DoesNotContain("radial-gradient", css); // tarjeta principal plana, sin brillos
    }

    [Theory]
    [InlineData("#16a34a", "#fff")]   // paleta de categorías: tonos medios, ícono blanco
    [InlineData("#ea580c", "#fff")]
    [InlineData("#64748b", "#fff")]
    [InlineData("#fde68a", "#14221c")] // color claro (de un respaldo): ícono oscuro
    [InlineData("#ffffff", "#14221c")]
    public void El_icono_de_categoria_siempre_contrasta_con_su_fondo(string fondo, string icono)
    {
        Assert.Equal(icono, ControlGastos.Web.Infrastructure.Fmt.ColorIconoSobre(fondo));
        string html = ControlGastos.Web.Infrastructure.Fmt.IconoGrupo("bi-cart", fondo).ToString()!;
        Assert.Contains($"style=\"background:{fondo};color:{icono}\"", html);
    }

    // ---------- Fase 3 ----------

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
            ["Nombre"] = "Diseño", ["Email"] = $"d{Guid.NewGuid():N}@test.local", ["Password"] = "clave1234", ["ConfirmarPassword"] = "clave1234",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return c;
    }

    [Theory]
    [InlineData(3, 6, 6, 3)]    // un segmento por cuota
    [InlineData(12, 12, 12, 12)]
    [InlineData(9, 9, 9, 9)]
    public void El_avance_de_cuotas_tiene_un_segmento_por_cuota(int hechas, int total, int segmentos, int llenos)
    {
        string html = ControlGastos.Web.Infrastructure.Fmt.ProgresoCuotas(hechas, total).ToString()!;
        Assert.Equal(segmentos, Regex.Matches(html, "<i").Count);
        Assert.Equal(llenos, Regex.Matches(html, "<i class=\"on\">").Count);
        Assert.Contains("aria-hidden=\"true\"", html);
    }

    [Fact]
    public void El_avance_de_cuotas_largo_es_una_barra_y_el_contado_no_tiene()
    {
        string largo = ControlGastos.Web.Infrastructure.Fmt.ProgresoCuotas(6, 24).ToString()!;
        Assert.Contains("<i class=\"barra\"><b style=\"width:25%\"></b></i>", largo);
        Assert.Equal("", ControlGastos.Web.Infrastructure.Fmt.ProgresoCuotas(1, 1).ToString());
    }

    [Fact]
    public async Task Nuevo_gasto_pliega_los_campos_secundarios_y_los_abre_si_hay_errores()
    {
        var c = await RegistrarAsync();
        string html = await c.GetStringAsync("/gastos/nuevo");
        string detalles = Regex.Match(html, "<details class=\"mas-detalles\"[^>]*>[\\s\\S]*?</details>").Value;
        Assert.DoesNotContain("open", Regex.Match(detalles, "<details[^>]*>").Value);
        foreach (var campo in new[] { "name=\"Fecha\"", "name=\"CategoriaId\"", "name=\"Comercio\"", "name=\"EnCuotas\"", "name=\"Notas\"" })
            Assert.Contains(campo, detalles);
        Assert.DoesNotContain("name=\"MontoCuota\"", detalles);       // lo principal queda a la vista
        Assert.DoesNotContain("name=\"TarjetaId\"", detalles);
        Assert.DoesNotContain("id=\"s-cierre\"", detalles);           // el aviso de cierre no se esconde

        // Con un error de validación vuelve abierto, para que se vea qué falta.
        var r = await PostFormAsync(c, "/gastos/nuevo", "/gastos/nuevo", new() { ["Descripcion"] = "", ["Fecha"] = "2026-10-10", ["Moneda"] = "UYU", ["MontoCuota"] = "100" });
        Assert.Matches("<details class=\"mas-detalles\" open", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task El_panel_es_un_bento_con_escalera_de_cuotas_y_vista_previa_si_esta_vacio()
    {
        var c = await RegistrarAsync();
        string vacio = await c.GetStringAsync("/panel");
        Assert.Contains("class=\"bento\"", vacio);
        Assert.Contains("class=\"vista-vacia vista-vacia-filas\"", vacio);
        Assert.DoesNotContain("class=\"escalera\"", vacio);    // sin cuotas a futuro no hay escalera

        string hoy = DateTime.Today.ToString("yyyy-MM-dd");
        await PostFormAsync(c, "/gastos/nuevo", "/gastos/nuevo", new()
        {
            ["Descripcion"] = "Heladera", ["Fecha"] = hoy, ["Moneda"] = "UYU", ["MontoCuota"] = "1000", ["EnCuotas"] = "true", ["CantidadCuotas"] = "6", ["CuotaActual"] = "1",
        });
        string lleno = await c.GetStringAsync("/panel");
        Assert.Contains("class=\"escalera\"", lleno);
        Assert.Contains("En cuotas", lleno);                    // desglose del total en pesos
    }

    // ---------- Fase 4 ----------

    [Fact]
    public async Task La_portada_es_el_inicio_de_sesion_y_entra_por_cuenta_login()
    {
        var c = factory.Cliente();
        string html = await c.GetStringAsync("/");
        Assert.Contains("<title>Mis·Gastos · Control de gastos personales</title>", html);
        Assert.Contains("action=\"/cuenta/login\"", html);
        Assert.Contains("href=\"/cuenta/registro\"", html);
        Assert.DoesNotContain("Bienvenido", html);

        // El formulario de la portada inicia sesión de verdad (mismo token y misma acción que /cuenta/login).
        string email = $"p{Guid.NewGuid():N}@test.local";
        await PostFormAsync(c, "/cuenta/registro", "/cuenta/registro", new() { ["Nombre"] = "Portada", ["Email"] = email, ["Password"] = "clave1234", ["ConfirmarPassword"] = "clave1234" });
        var otro = factory.Cliente();
        var r = await PostFormAsync(otro, "/", "/cuenta/login", new() { ["Email"] = email, ["Password"] = "clave1234" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/panel", r.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task El_login_explica_que_hacer_si_olvidaste_la_contrasena()
    {
        string html = await factory.Cliente().GetStringAsync("/cuenta/login");
        string olvido = Regex.Match(html, "<details class=\"olvido[\\s\\S]*?</details>").Value;
        Assert.Contains("¿Olvidaste tu contraseña?", olvido);
        Assert.Contains("contraseña temporal", olvido);
        Assert.DoesNotContain("href", olvido); // solo texto: no hay recuperación por correo
    }

    [Fact]
    public async Task La_tarjeta_de_ejemplo_usa_importes_inventados()
    {
        string html = await factory.Cliente().GetStringAsync("/cuenta/login");
        string tarjeta = Regex.Match(html, "<div class=\"hero-card\"[\\s\\S]*?<div class=\"auth-foot\"").Value;
        Assert.Contains("18.320", tarjeta);
        Assert.Contains("Heladera", tarjeta);
        Assert.Contains("aria-hidden=\"true\"", tarjeta);
    }

    [Fact]
    public async Task El_registro_muestra_los_requisitos_de_la_contrasena()
    {
        string html = await factory.Cliente().GetStringAsync("/cuenta/registro");
        Assert.Contains("aria-describedby=\"requisitos-pass\"", html);
        foreach (var regla in new[] { "largo", "minuscula", "numero" })
            Assert.Contains($"data-regla=\"{regla}\"", html);
        string js = await factory.Cliente().GetStringAsync("/js/site.js");
        Assert.Contains("v.length >= 8", js); // mismas reglas que Identity (Program.cs)
    }
}
