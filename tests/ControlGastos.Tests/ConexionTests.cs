using ControlGastos.Web.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ControlGastos.Tests;

/// <summary>De dónde sale la cadena de conexión en producción (Vercel + Neon).</summary>
public class ConexionTests
{
    // Formato que inyecta Neon (los datos son inventados).
    private const string UrlNeon = "postgresql://neondb_owner:Cl4ve%2Fsecreta@ep-cool-name-a1b2c3-pooler.sa-east-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require";

    private static IConfiguration Config(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => v.Clave, v => (string?)v.Valor)).Build();

    private static NpgsqlConnectionStringBuilder Leer(IConfiguration c) => new(ConfiguracionBase.ObtenerConexion(c));

    [Fact]
    public void Interpreta_la_URL_de_Neon()
    {
        var b = Leer(Config(("DATABASE_URL", UrlNeon)));
        Assert.Equal("ep-cool-name-a1b2c3-pooler.sa-east-1.aws.neon.tech", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("neondb", b.Database);
        Assert.Equal("neondb_owner", b.Username);
        Assert.Equal("Cl4ve/secreta", b.Password);
        Assert.Equal(SslMode.Require, b.SslMode);
    }

    [Theory]
    [InlineData("POSTGRES_URL")]
    [InlineData("DATABASE_URL_UNPOOLED")]
    [InlineData("POSTGRES_URL_NON_POOLING")]
    [InlineData("NEON_DATABASE_URL")]      // integración con prefijo personalizado
    [InlineData("STORAGE_POSTGRES_URL")]
    public void Acepta_los_otros_nombres_de_la_integracion(string variable)
    {
        Assert.Equal("neondb", Leer(Config((variable, UrlNeon))).Database);
    }

    [Fact]
    public void DATABASE_URL_tiene_prioridad_sobre_las_demas()
    {
        var b = Leer(Config(("POSTGRES_URL", UrlNeon.Replace("neondb?", "otra?")), ("DATABASE_URL", UrlNeon)));
        Assert.Equal("neondb", b.Database);
    }

    [Fact]
    public void ConnectionStrings_Default_acepta_formato_Npgsql_o_URL()
    {
        Assert.Equal("Host=h;Database=d", ConfiguracionBase.ObtenerConexion(Config(("ConnectionStrings:Default", "Host=h;Database=d"))));
        Assert.Equal("neondb", Leer(Config(("ConnectionStrings:Default", UrlNeon))).Database);
    }

    [Fact]
    public void Sin_conexion_explica_que_falta_y_muestra_solo_nombres()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfiguracionBase.ObtenerConexion(Config(("PGHOST", "ep-secreto.neon.tech"), ("PGPASSWORD", "no-mostrar-esto"))));
        Assert.Contains("DATABASE_URL", ex.Message);
        Assert.Contains("Redeploy", ex.Message);
        Assert.Contains("PGHOST", ex.Message);
        Assert.Contains("PGPASSWORD", ex.Message);
        Assert.DoesNotContain("no-mostrar-esto", ex.Message);
        Assert.DoesNotContain("ep-secreto", ex.Message);
    }
}
