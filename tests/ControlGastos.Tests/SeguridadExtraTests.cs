using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using ControlGastos.Web.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ControlGastos.Tests;

/// <summary>Revisión de seguridad previa al despliegue: ReDoS, registro restringido, hash y encabezados.</summary>
public class SeguridadExtraTests
{
    [Fact]
    public void El_parser_no_se_traba_con_lineas_hostiles()
    {
        // Antes de la corrección, una línea de 4.000 caracteres así tardaba más de 70 segundos.
        string[] hostiles =
        {
            new string(' ', 4000) + "a 01/01/26 pesos 1,0 zz",
            "a" + string.Concat(Enumerable.Repeat("\t ", 2000)) + "01/01/26 pesos 1,0 zz",
            string.Concat(Enumerable.Repeat("a ", 145)) + "01/01/26 pesos 1,0 zz",
            string.Concat(Enumerable.Repeat("x 01/01/26 pesos ", 17)),
        };
        var sw = Stopwatch.StartNew();
        foreach (var h in hostiles)
            Assert.Equal(TipoLinea.NoReconocida, EstadoDeCuenta.InterpretarLinea(1, h).Tipo);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Tardó {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Un_texto_maximo_se_procesa_rapido()
    {
        string texto = string.Concat(Enumerable.Repeat(new string(' ', 290) + "a 01/01/26 pesos 1,0 zz\n", 100));
        var sw = Stopwatch.StartNew();
        EstadoDeCuenta.Interpretar(texto);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"Tardó {sw.ElapsedMilliseconds} ms");
    }
}

/// <summary>App con registro restringido a una lista de correos.</summary>
public class AppConListaFactory : AppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("App:CorreosPermitidos", "yo@test.local; Amigo@Test.local, hash@test.local");
    }
}

public class RegistroRestringidoTests(AppConListaFactory factory) : IClassFixture<AppConListaFactory>
{
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

    private async Task<HttpResponseMessage> RegistrarAsync(string email, string password = "clave1234")
    {
        var c = factory.Cliente();
        var html = await c.GetStringAsync("/cuenta/registro");
        return await c.PostAsync("/cuenta/registro", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TokenRegex.Match(html).Groups[1].Value,
            ["Nombre"] = "Test", ["Email"] = email, ["Password"] = password, ["ConfirmarPassword"] = password,
        }));
    }

    [Fact]
    public async Task Solo_los_correos_de_la_lista_pueden_registrarse()
    {
        var ajeno = await RegistrarAsync("desconocido@test.local");
        Assert.Equal(HttpStatusCode.OK, ajeno.StatusCode);
        Assert.Contains("no está habilitado", await ajeno.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Redirect, (await RegistrarAsync("yo@test.local")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await RegistrarAsync("amigo@test.local")).StatusCode); // sin distinguir mayúsculas
    }

    [Fact]
    public async Task El_hash_usa_210000_iteraciones()
    {
        const string email = "hash@test.local";
        await RegistrarAsync(email, "OtraClave123");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ControlGastos.Web.Domain.AppUser>>();
        var user = await users.FindByEmailAsync(email);
        var bytes = Convert.FromBase64String(user!.PasswordHash!);
        // Formato v3: [0x01][PRF 4 bytes][iteraciones 4 bytes][largo del salt 4 bytes][salt][subclave]
        int iteraciones = (bytes[5] << 24) | (bytes[6] << 16) | (bytes[7] << 8) | bytes[8];
        Assert.Equal(210_000, iteraciones);
    }

    [Fact]
    public async Task Incluye_Cross_Origin_Opener_Policy()
    {
        var r = await factory.Cliente().GetAsync("/cuenta/login");
        Assert.Equal("same-origin", r.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
    }
}
