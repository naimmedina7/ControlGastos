using System.Net;
using System.Text.RegularExpressions;

namespace ControlGastos.Tests;

/// <summary>Reportes, importación, CSV, tipo de cambio y presupuestos por HTTP, con la app completa.</summary>
public class Etapa4HttpTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient c, string formUrl, string postUrl, Dictionary<string, string> datos)
    {
        var html = await c.GetStringAsync(formUrl);
        datos["__RequestVerificationToken"] = TokenRegex.Match(html).Groups[1].Value;
        return await c.PostAsync(postUrl, new FormUrlEncodedContent(datos));
    }

    private async Task<HttpClient> RegistrarAsync()
    {
        var c = factory.Cliente();
        string email = $"u{Guid.NewGuid():N}@test.local";
        var r = await PostFormAsync(c, "/cuenta/registro", "/cuenta/registro", new()
        {
            ["Nombre"] = "Test", ["Email"] = email, ["Password"] = "clave1234", ["ConfirmarPassword"] = "clave1234",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return c;
    }

    /// <summary>Campos de un formulario de la página (inputs con name y value), como los enviaría el navegador.</summary>
    private static Dictionary<string, string> CamposDe(string html, string formId)
    {
        string form = Regex.Match(html, "<form[^>]*id=\"" + formId + "\"[\\s\\S]*?</form>").Value;
        var campos = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(form, "<input[^>]*>"))
        {
            string tag = m.Value;
            string name = Regex.Match(tag, "name=\"([^\"]+)\"").Groups[1].Value;
            if (name.Length == 0) continue;
            if (tag.Contains("type=\"checkbox\"") && !tag.Contains("checked")) continue;
            campos[name] = WebUtility.HtmlDecode(Regex.Match(tag, "value=\"([^\"]*)\"").Groups[1].Value);
        }
        return campos;
    }

    [Fact]
    public async Task Importar_analiza_confirma_y_no_acepta_montos_del_formulario()
    {
        var c = await RegistrarAsync();
        var analizar = await PostFormAsync(c, "/importar", "/importar/analizar", new() { ["Texto"] = Muestras.Octubre, ["TarjetaId"] = "", ["MesEstado"] = "" });
        string html = await analizar.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, analizar.StatusCode);
        Assert.Contains("estado de cuenta de octubre 2026", html);

        var campos = CamposDe(html, "form-confirmar");
        Assert.Contains("Items[0].Numero", campos.Keys);
        Assert.Equal(Muestras.Octubre.Replace("\r", ""), campos["Texto"].Replace("\r", "")); // el texto viaja intacto
        // Aunque alguien agregue un monto al formulario, el servidor vuelve a leer el texto y lo ignora.
        campos["Items[0].Monto"] = "999999";
        var confirmar = await c.PostAsync("/importar/confirmar", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.Redirect, confirmar.StatusCode);
        Assert.Contains("/gastos?mes=2026-10", confirmar.Headers.Location!.ToString());

        string mes = await c.GetStringAsync("/gastos?mes=2026-10");
        Assert.Contains("$ 13.933,48", mes);
        Assert.Contains("US$ 37,44", mes);
        Assert.DoesNotContain("999.999", mes);

        // Analizar de nuevo el mismo texto: todo aparece como ya cargado.
        var otra = await PostFormAsync(c, "/importar", "/importar/analizar", new() { ["Texto"] = Muestras.Octubre, ["TarjetaId"] = "" });
        Assert.Contains("Ya cargado", await otra.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Exportar_CSV_devuelve_solo_los_gastos_del_usuario()
    {
        var ana = await RegistrarAsync();
        await PostFormAsync(ana, "/gastos/nuevo", "/gastos/nuevo", new()
        {
            ["Descripcion"] = "=Gasto de Ana", ["Fecha"] = "2026-10-10", ["Moneda"] = "UYU", ["MontoCuota"] = "1234.5",
        });
        var r = await ana.GetAsync("/reportes/exportar?desde=2026-10&hasta=2026-10");
        Assert.Equal("text/csv", r.Content.Headers.ContentType!.MediaType);
        string csv = await r.Content.ReadAsStringAsync();
        Assert.Contains("'=Gasto de Ana", csv);   // neutralizado contra inyección de fórmulas
        Assert.Contains("1234,50", csv);

        var beto = await RegistrarAsync();
        Assert.DoesNotContain("Gasto de Ana", await beto.GetStringAsync("/reportes/exportar?desde=2026-10&hasta=2026-10"));
    }

    [Fact]
    public async Task Reportes_carga_Chart_js_con_integridad_y_sin_scripts_inline()
    {
        var c = await RegistrarAsync();
        string html = await c.GetStringAsync("/reportes");
        Assert.Contains("integrity=\"sha384-", html);
        Assert.Contains("data-chart=\"meses\"", html);
        Assert.DoesNotMatch(new Regex("<script(\\s[^>]*)?>[^<]+</script>"), html);
    }

    [Fact]
    public async Task Tipo_de_cambio_y_presupuestos_son_por_usuario()
    {
        var ana = await RegistrarAsync();
        await PostFormAsync(ana, "/tipo-cambio", "/tipo-cambio", new() { ["Form.Mes"] = "2026-10", ["Form.UsdEnPesos"] = "41.37" });
        string paginaPres = await ana.GetStringAsync("/presupuestos");
        string catId = Regex.Match(paginaPres, "<option value=\"(\\d+)\">Delivery y comidas</option>").Groups[1].Value;
        await PostFormAsync(ana, "/presupuestos", "/presupuestos", new() { ["Form.CategoriaId"] = catId, ["Form.Moneda"] = "UYU", ["Form.MontoMensual"] = "4000" });

        string tcAna = await ana.GetStringAsync("/tipo-cambio");
        Assert.Contains("41,37", tcAna);
        string presAna = await ana.GetStringAsync("/presupuestos");
        Assert.Contains("$ 4.000,00", presAna);
        string idPres = Regex.Match(presAna, "/presupuestos/(\\d+)/eliminar").Groups[1].Value;
        string idTc = Regex.Match(tcAna, "/tipo-cambio/(\\d+)/eliminar").Groups[1].Value;

        var beto = await RegistrarAsync();
        Assert.DoesNotContain("41,37", await beto.GetStringAsync("/tipo-cambio"));
        Assert.DoesNotContain("$ 4.000,00", await beto.GetStringAsync("/presupuestos"));
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(beto, "/presupuestos", $"/presupuestos/{idPres}/eliminar", new())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(beto, "/tipo-cambio", $"/tipo-cambio/{idTc}/eliminar", new())).StatusCode);

        // Beto no puede presupuestar la categoría de Ana.
        var ajena = await PostFormAsync(beto, "/presupuestos", "/presupuestos", new() { ["Form.CategoriaId"] = catId, ["Form.Moneda"] = "UYU", ["Form.MontoMensual"] = "1" });
        Assert.Equal(HttpStatusCode.OK, ajena.StatusCode);
        Assert.Contains("La categoría seleccionada no existe.", await ajena.Content.ReadAsStringAsync());
    }
}
