using System.Text;
using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Tests;

/// <summary>
/// PostgreSQL (Npgsql) rechaza DateTime con Kind=Unspecified en columnas timestamptz. Un respaldo exportado
/// desde SQLite trae las fechas sin zona; restaurarlo en producción daba error 500. Estos tests cubren el arreglo.
/// </summary>
public class FechasUtcTests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    public FechasUtcTests() => _conn.Open();
    public void Dispose() => _conn.Dispose();

    [Fact]
    public void El_conversor_deja_toda_fecha_en_UTC()
    {
        var conv = new FechaUtcConverter();
        var sinZona = new DateTime(2026, 10, 22, 15, 30, 0, DateTimeKind.Unspecified);
        var utc = (DateTime)conv.ConvertToProvider(sinZona)!;
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(sinZona.Ticks, utc.Ticks); // una fecha sin zona se interpreta como UTC, sin moverla

        var local = new DateTime(2026, 10, 22, 15, 30, 0, DateTimeKind.Local);
        Assert.Equal(local.ToUniversalTime(), (DateTime)conv.ConvertToProvider(local)!);

        var leida = (DateTime)conv.ConvertFromProvider(sinZona)!;
        Assert.Equal(DateTimeKind.Utc, leida.Kind);
    }

    [Fact]
    public void El_modelo_de_PostgreSQL_aplica_el_conversor_a_todas_las_fechas()
    {
        using var db = new PostgresDesignFactory().CreateDbContext(Array.Empty<string>());
        var props = db.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)).ToList();
        Assert.NotEmpty(props);
        Assert.All(props, p => Assert.True(
            p.GetValueConverter() is FechaUtcConverter or FechaUtcNullableConverter,
            $"{p.DeclaringType.DisplayName()}.{p.Name} no tiene el conversor UTC"));
    }

    [Fact]
    public async Task Un_respaldo_con_fechas_sin_zona_se_restaura_y_queda_en_UTC()
    {
        var opciones = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options;
        await using (var setup = new SqliteAppDbContext(opciones, new UsuarioFijo(null)))
        {
            setup.Database.EnsureCreated();
            setup.Users.Add(new AppUser { Id = "u1", UserName = "u1@test", Email = "u1@test" });
            await setup.SaveChangesAsync();
        }

        // "creadoEn" sin la Z final: así lo exporta una base SQLite.
        const string json = """
            {"app":"Mis·Gastos","version":1,"exportadoEn":"2026-10-23T00:00:00",
             "tarjetas":[],"categorias":[],"gastosFijos":[],"tiposCambio":[],"presupuestos":[],
             "gastos":[{"descripcion":"Antel","comercio":"ANTEL EN LINEA","fecha":"2026-10-13","tarjetaId":null,"categoriaId":null,
                        "moneda":"UYU","montoCuota":65,"cantidadCuotas":1,"primerPeriodo":"2026-10-01","notas":null,
                        "recurrenteId":null,"creadoEn":"2026-10-22T23:13:36.123"}]}
            """;
        var respaldo = RespaldoService.Leer(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(DateTimeKind.Unspecified, respaldo.Gastos[0].CreadoEn.Kind); // así llega del archivo

        await using var db = new SqliteAppDbContext(opciones, new UsuarioFijo("u1"));
        await new RespaldoService(db).RestaurarAsync(respaldo);

        await using var db2 = new SqliteAppDbContext(opciones, new UsuarioFijo("u1"));
        var g = await db2.Gastos.SingleAsync();
        Assert.Equal(DateTimeKind.Utc, g.CreadoEn.Kind);
        Assert.Equal(new DateTime(2026, 10, 22, 23, 13, 36, 123), g.CreadoEn, TimeSpan.Zero);
    }
}
