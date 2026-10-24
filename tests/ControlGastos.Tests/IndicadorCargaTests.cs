using System.Net;
using System.Text.RegularExpressions;

namespace ControlGastos.Tests;

/// <summary>El indicador de carga está en todas las páginas y las descargas no lo activan.</summary>
public class IndicadorCargaTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

    [Fact]
    public async Task Las_paginas_publicas_y_privadas_incluyen_la_barra_y_el_aviso_accesible()
    {
        Assert.Contains("class=\"carga-barra\"", await factory.Cliente().GetStringAsync("/cuenta/login"));

        var c = factory.Cliente();
        string email = $"u{Guid.NewGuid():N}@test.local";
        var html = await c.GetStringAsync("/cuenta/registro");
        var r = await c.PostAsync("/cuenta/registro", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRegex.Match(html).Groups[1].Value,
            ["Nombre"] = "Test", ["Email"] = email, ["Password"] = "clave1234", ["ConfirmarPassword"] = "clave1234",
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        string panel = await c.GetStringAsync("/panel");
        Assert.Contains("class=\"carga-barra\"", panel);
        Assert.Contains("role=\"status\" aria-live=\"polite\" data-carga-estado", panel);

        // Las descargas no cambian de página: no deben dejar la barra de carga encendida.
        AssertDescarga(await c.GetStringAsync("/cuenta/perfil"), "/cuenta/respaldo");
        AssertDescarga(await c.GetStringAsync("/gastos"), "/reportes/exportar");
        AssertDescarga(await c.GetStringAsync("/reportes"), "/reportes/exportar");
    }

    /// <summary>El enlace que apunta a <paramref name="ruta"/> tiene download y data-sin-carga (en cualquier orden).</summary>
    private static void AssertDescarga(string html, string ruta)
    {
        var enlace = Regex.Matches(html, @"<a\s[^>]*>").Select(m => m.Value).FirstOrDefault(a => a.Contains($"href=\"{ruta}"));
        Assert.NotNull(enlace);
        Assert.Contains(" download", enlace);
        Assert.Contains("data-sin-carga", enlace);
    }

    [Fact]
    public async Task El_script_bloquea_el_segundo_envio_sin_deshabilitar_el_boton()
    {
        string js = await factory.Cliente().GetStringAsync("/js/site.js");
        Assert.Contains("if (form.dataset.enviando) { e.preventDefault(); return; }", js);
        Assert.DoesNotContain("boton.disabled = true", js); // deshabilitarlo haría perder el valor de "Guardar y cargar otro"
    }
}
