using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Tests;

/// <summary>
/// Verifica el aislamiento por usuario directamente sobre el DbContext (SQLite en memoria):
/// cada usuario ve, modifica y borra solo sus propios datos.
/// </summary>
public class AislamientoTests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private const string Ana = "user-ana";
    private const string Beto = "user-beto";

    public AislamientoTests()
    {
        _conn.Open();
        using var db = Contexto(null);
        db.Database.EnsureCreated();
        db.Users.AddRange(
            new AppUser { Id = Ana, UserName = "ana@test", Email = "ana@test" },
            new AppUser { Id = Beto, UserName = "beto@test", Email = "beto@test" });
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    private SqliteAppDbContext Contexto(string? userId) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options, new UsuarioFijo(userId));

    private async Task<int> CrearGastoDeAnaAsync()
    {
        await using var db = Contexto(Ana);
        var tarjeta = new Tarjeta { Nombre = "Visa", Tipo = TipoTarjeta.Credito };
        db.Tarjetas.Add(tarjeta);
        await db.SaveChangesAsync();
        var g = await new GastoService(db).CrearAsync(new GastoInput("Heladera", null, new DateOnly(2026, 10, 1), tarjeta.Id, null, Moneda.UYU, 1000m, 6, 1, null));
        return g.Id;
    }

    [Fact]
    public async Task Los_datos_nuevos_toman_el_usuario_actual_como_dueño()
    {
        int id = await CrearGastoDeAnaAsync();
        await using var db = Contexto(Ana);
        var g = await db.Gastos.Include(x => x.Cuotas).SingleAsync(x => x.Id == id);
        Assert.Equal(Ana, g.UserId);
        Assert.All(g.Cuotas, c => Assert.Equal(Ana, c.UserId));
    }

    [Fact]
    public async Task Otro_usuario_no_ve_los_gastos_ni_las_cuotas()
    {
        int id = await CrearGastoDeAnaAsync();
        await using var db = Contexto(Beto);

        Assert.Null(await db.Gastos.FirstOrDefaultAsync(g => g.Id == id));
        Assert.Empty(await db.Gastos.ToListAsync());
        Assert.Empty(await db.Cuotas.ToListAsync());
        Assert.Empty(await db.Tarjetas.ToListAsync());

        var resumen = await new GastoService(db).ResumenAsync(new DateOnly(2026, 10, 1));
        Assert.Empty(resumen.Lineas);
    }

    [Fact]
    public async Task Sin_usuario_no_se_ve_ningun_dato()
    {
        await CrearGastoDeAnaAsync();
        await using var db = Contexto(null);
        Assert.Empty(await db.Gastos.ToListAsync());
    }

    [Fact]
    public async Task Otro_usuario_no_puede_editar_ni_borrar()
    {
        int id = await CrearGastoDeAnaAsync();
        await using var db = Contexto(Beto);
        var service = new GastoService(db);

        Assert.False(await service.ActualizarAsync(id, new GastoInput("Hackeado", null, new DateOnly(2026, 10, 1), null, null, Moneda.UYU, 1m, 1, 1, null)));
        Assert.False(await service.EliminarAsync(id));

        await using var dbAna = Contexto(Ana);
        Assert.Equal("Heladera", (await dbAna.Gastos.SingleAsync(g => g.Id == id)).Descripcion);
    }

    [Fact]
    public async Task No_se_puede_usar_la_tarjeta_de_otro_usuario()
    {
        await CrearGastoDeAnaAsync();
        int tarjetaDeAna;
        await using (var dbAna = Contexto(Ana)) tarjetaDeAna = (await dbAna.Tarjetas.SingleAsync()).Id;

        await using var db = Contexto(Beto);
        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            new GastoService(db).CrearAsync(new GastoInput("Compra", null, new DateOnly(2026, 10, 1), tarjetaDeAna, null, Moneda.UYU, 10m, 1, 1, null)));
        Assert.Contains("tarjeta", ex.Message);
    }

    [Fact]
    public async Task Guardar_una_fila_ajena_adjuntada_a_mano_falla()
    {
        int id = await CrearGastoDeAnaAsync();
        await using var db = Contexto(Beto);
        // Aunque alguien saltee el filtro y adjunte la entidad, SaveChanges la rechaza.
        var ajeno = new Gasto { Id = id, UserId = Ana, Descripcion = "x", MontoCuota = 1, CantidadCuotas = 1 };
        db.Attach(ajeno).State = EntityState.Modified;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task No_se_puede_crear_un_dato_a_nombre_de_otro()
    {
        await using var db = Contexto(Beto);
        db.Tarjetas.Add(new Tarjeta { Nombre = "Trampa", Tipo = TipoTarjeta.Debito, UserId = Ana });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Editar_regenera_las_cuotas()
    {
        int id = await CrearGastoDeAnaAsync();
        await using var db = Contexto(Ana);
        Assert.True(await new GastoService(db).ActualizarAsync(id,
            new GastoInput("Heladera", null, new DateOnly(2026, 10, 1), null, null, Moneda.UYU, 500m, 3, 2, null)));

        await using var db2 = Contexto(Ana);
        var cuotas = await db2.Cuotas.Where(c => c.GastoId == id).OrderBy(c => c.Numero).ToListAsync();
        Assert.Equal(3, cuotas.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), cuotas[0].Periodo); // cuota 2 en octubre → la 1 en septiembre
        Assert.All(cuotas, c => Assert.Equal(500m, c.Monto));
    }

    [Fact]
    public async Task El_resumen_separa_pesos_y_dolares()
    {
        await using var db = Contexto(Ana);
        var s = new GastoService(db);
        var oct = new DateOnly(2026, 10, 10);
        await s.CrearAsync(new GastoInput("Netflix", null, oct, null, null, Moneda.USD, 5.17m, 1, 1, null));
        await s.CrearAsync(new GastoInput("Antel", null, oct, null, null, Moneda.UYU, 65m, 1, 1, null));
        await s.CrearAsync(new GastoInput("Muebles", null, oct, null, null, Moneda.UYU, 998.33m, 6, 1, null));

        var r = await s.ResumenAsync(oct);
        Assert.Equal(1063.33m, r.Total(Moneda.UYU));
        Assert.Equal(5.17m, r.Total(Moneda.USD));
        Assert.Equal(998.33m * 5, r.Pendiente(Moneda.UYU));

        var proximos = await s.ProyeccionAsync(oct, 3);
        Assert.Equal(new[] { 1063.33m, 998.33m, 998.33m }, proximos.Select(p => p.UYU));
    }
}
