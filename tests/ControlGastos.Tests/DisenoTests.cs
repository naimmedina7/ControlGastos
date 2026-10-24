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
}
