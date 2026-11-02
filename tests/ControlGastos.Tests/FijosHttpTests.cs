using System.Net;
using System.Text.RegularExpressions;

namespace ControlGastos.Tests;

/// <summary>Gastos fijos por HTTP: se cargan también en los meses futuros que miras.</summary>
public class FijosHttpTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient c, string formUrl, string postUrl, Dictionary<string, string> datos)
    {
        datos["__RequestVerificationToken"] = TokenRegex.Match(await c.GetStringAsync(formUrl)).Groups[1].Value;
        return await c.PostAsync(postUrl, new FormUrlEncodedContent(datos));
    }

    [Fact]
    public async Task Al_avanzar_a_un_mes_futuro_ya_se_ven_los_gastos_fijos()
    {
        var c = factory.Cliente();
        var reg = await PostFormAsync(c, "/cuenta/registro", "/cuenta/registro", new()
        {
            ["Nombre"] = "Fijos", ["Email"] = $"f{Guid.NewGuid():N}@test.local", ["Password"] = "clave1234", ["ConfirmarPassword"] = "clave1234",
        });
        Assert.Equal(HttpStatusCode.Redirect, reg.StatusCode);

        var hoy = DateTime.Today;
        var crear = await PostFormAsync(c, "/fijos/nuevo", "/fijos/nuevo", new()
        {
            ["Descripcion"] = "Alquiler futuro", ["Monto"] = "15000", ["Moneda"] = "UYU", ["DiaDelMes"] = "1",
            ["DesdeMes"] = hoy.ToString("yyyy-MM"), ["TarjetaId"] = "", ["CategoriaId"] = "",
        });
        Assert.Equal(HttpStatusCode.Redirect, crear.StatusCode);

        string futuro = hoy.AddMonths(3).ToString("yyyy-MM");
        string html = await c.GetStringAsync($"/gastos?mes={futuro}");
        Assert.Contains("Alquiler futuro", html);
        Assert.DoesNotContain("Gastos fijos previstos", html);   // ya está cargado, no "previsto"
        Assert.Contains("15.000", Regex.Match(html, "<section class=\"kpi-grid[\\s\\S]*?</section>").Value);

        // Y en el inicio de ese mes también suma.
        Assert.Contains("15.000", await c.GetStringAsync($"/panel?mes={futuro}"));
    }
}
