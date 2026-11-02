using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Tests;

/// <summary>Gastos fijos (recurrentes), día de cierre y planes activos, sobre SQLite en memoria.</summary>
public class RecurrentesTests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private const string Ana = "user-ana";
    private const string Beto = "user-beto";
    private static readonly DateOnly Oct = new(2026, 10, 1);

    public RecurrentesTests()
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

    private static RecurrenteInput Fijo(string desc, decimal monto, DateOnly desde, DateOnly? hasta = null, int dia = 1, int? tarjeta = null) =>
        new(desc, tarjeta, null, Moneda.UYU, monto, dia, desde, hasta, null);

    private async Task<List<Gasto>> GastosDeAsync(string user, int recurrenteId)
    {
        await using var db = Contexto(user);
        return await db.Gastos.Where(g => g.GastoRecurrenteId == recurrenteId).OrderBy(g => g.PrimerPeriodo).ToListAsync();
    }

    [Fact]
    public async Task Al_crear_con_inicio_pasado_genera_hasta_el_mes_actual()
    {
        await using var db = Contexto(Ana);
        var r = await new RecurrenteService(db).CrearAsync(Fijo("Facultad", 9000m, new DateOnly(2026, 8, 1)), Oct);

        var gastos = await GastosDeAsync(Ana, r.Id);
        Assert.Equal(new[] { new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), Oct }, gastos.Select(g => g.PrimerPeriodo));
        Assert.All(gastos, g => Assert.Equal(9000m, g.MontoCuota));
        Assert.Equal(Oct, r.GeneradoHasta);
    }

    [Fact]
    public async Task Generar_es_idempotente_y_avanza_mes_a_mes()
    {
        await using var db = Contexto(Ana);
        var s = new RecurrenteService(db);
        var r = await s.CrearAsync(Fijo("Préstamo moto", 5000m, Oct), Oct);

        Assert.Equal(0, await s.GenerarPendientesAsync(Oct));
        Assert.Equal(0, await s.GenerarPendientesAsync(Oct));
        Assert.Single(await GastosDeAsync(Ana, r.Id));

        Assert.Equal(1, await s.GenerarPendientesAsync(Oct.AddMonths(1)));
        Assert.Equal(2, (await GastosDeAsync(Ana, r.Id)).Count);
    }

    [Theory]
    [InlineData(25, 28, 28)]   // la tarjeta cambió el día de cierre después de generar octubre
    [InlineData(30, 31, 30)]   // día 31: el 31/10 cae después del cierre, el 30/11 (acotado) no
    public async Task Un_fijo_de_credito_que_choca_no_frena_a_los_de_debito(int cierreInicial, int dia, int cierreFinal)
    {
        int credito; int[] debito;
        await using (var db = Contexto(Ana))
        {
            var visa = new Tarjeta { Nombre = "Visa", Tipo = TipoTarjeta.Credito, DiaCierre = cierreInicial };
            var itau = new Tarjeta { Nombre = "Itaú débito", Tipo = TipoTarjeta.Debito };
            db.Tarjetas.AddRange(visa, itau);
            await db.SaveChangesAsync();
            var s = new RecurrenteService(db);
            // El de crédito cae después del cierre: el de octubre ya va al estado de cuenta de noviembre.
            credito = (await s.CrearAsync(Fijo("Seguro", 800m, Oct, dia: dia, tarjeta: visa.Id), Oct)).Id;
            debito = new[]
            {
                (await s.CrearAsync(Fijo("Alquiler", 15000m, Oct, tarjeta: itau.Id), Oct)).Id,
                (await s.CrearAsync(Fijo("Internet", 1200m, Oct, tarjeta: itau.Id), Oct)).Id,
                (await s.CrearAsync(Fijo("Gimnasio", 1500m, Oct, tarjeta: itau.Id), Oct)).Id,
            };
            visa.DiaCierre = cierreFinal;
            await db.SaveChangesAsync();
        }
        Assert.Equal(new DateOnly(2026, 11, 1), (await GastosDeAsync(Ana, credito)).Single().PrimerPeriodo);

        // Llega noviembre: el de crédito de noviembre caería en el mismo estado de cuenta que el de octubre.
        await using (var db = Contexto(Ana))
            await new RecurrenteService(db).GenerarPendientesAsync(Oct.AddMonths(1));

        foreach (var id in debito)
            Assert.Contains(await GastosDeAsync(Ana, id), g => g.PrimerPeriodo == new DateOnly(2026, 11, 1));
        Assert.Single(await GastosDeAsync(Ana, credito), g => g.PrimerPeriodo == new DateOnly(2026, 11, 1)); // sin duplicar
    }

    [Fact]
    public async Task Un_mes_borrado_no_vuelve_a_aparecer()
    {
        int id;
        await using (var db = Contexto(Ana))
            id = (await new RecurrenteService(db).CrearAsync(Fijo("Gimnasio", 1500m, Oct), Oct)).Id;

        var gastoOct = (await GastosDeAsync(Ana, id)).Single();
        await using (var db = Contexto(Ana))
            Assert.True(await new GastoService(db).EliminarAsync(gastoOct.Id));

        await using (var db = Contexto(Ana))
            Assert.Equal(0, await new RecurrenteService(db).GenerarPendientesAsync(Oct));
        Assert.Empty(await GastosDeAsync(Ana, id));
    }

    [Fact]
    public async Task Respeta_el_mes_final()
    {
        await using var db = Contexto(Ana);
        var s = new RecurrenteService(db);
        var r = await s.CrearAsync(Fijo("Curso", 2000m, new DateOnly(2026, 9, 1), hasta: Oct), Oct);
        await s.GenerarPendientesAsync(new DateOnly(2027, 3, 1));
        Assert.Equal(2, (await GastosDeAsync(Ana, r.Id)).Count);
    }

    [Fact]
    public async Task Pausado_no_genera_y_al_reanudar_sigue_desde_el_mes_actual()
    {
        await using var db = Contexto(Ana);
        var s = new RecurrenteService(db);
        var r = await s.CrearAsync(Fijo("Spotify", 300m, Oct), Oct);

        await s.CambiarEstadoAsync(r.Id, Oct);                      // pausa en octubre
        Assert.Equal(0, await s.GenerarPendientesAsync(new DateOnly(2026, 12, 1)));

        await s.CambiarEstadoAsync(r.Id, new DateOnly(2027, 1, 1)); // reanuda en enero
        var periodos = (await GastosDeAsync(Ana, r.Id)).Select(g => g.PrimerPeriodo).ToList();
        Assert.Equal(new[] { Oct, new DateOnly(2027, 1, 1) }, periodos); // noviembre y diciembre no se cargan
    }

    [Fact]
    public async Task El_dia_se_acota_al_largo_del_mes()
    {
        await using var db = Contexto(Ana);
        var r = await new RecurrenteService(db).CrearAsync(Fijo("Alquiler", 20000m, new DateOnly(2027, 2, 1), dia: 31), new DateOnly(2027, 2, 1));
        Assert.Equal(new DateOnly(2027, 2, 28), (await GastosDeAsync(Ana, r.Id)).Single().Fecha);
    }

    [Fact]
    public async Task Editar_aplica_a_los_meses_siguientes_sin_tocar_los_cargados()
    {
        await using var db = Contexto(Ana);
        var s = new RecurrenteService(db);
        var r = await s.CrearAsync(Fijo("Facultad", 9000m, Oct), Oct);
        Assert.True(await s.ActualizarAsync(r.Id, Fijo("Facultad", 9500m, Oct), Oct));
        await s.GenerarPendientesAsync(Oct.AddMonths(1));

        Assert.Equal(new[] { 9000m, 9500m }, (await GastosDeAsync(Ana, r.Id)).Select(g => g.MontoCuota));
    }

    [Fact]
    public async Task Eliminar_conserva_los_gastos_ya_cargados()
    {
        await using var db = Contexto(Ana);
        var s = new RecurrenteService(db);
        var r = await s.CrearAsync(Fijo("Netflix", 400m, new DateOnly(2026, 9, 1)), Oct);
        Assert.Equal(2, await s.EliminarAsync(r.Id, Oct));

        await using var db2 = Contexto(Ana);
        Assert.Equal(2, await db2.Gastos.CountAsync(g => g.Descripcion == "Netflix" && g.GastoRecurrenteId == null));
        Assert.Empty(await db2.GastosRecurrentes.ToListAsync());
    }

    // ---------- Meses futuros ----------

    [Theory]
    [InlineData("2026-08-01", "2026-10-01")]   // un mes pasado: hasta el actual
    [InlineData("2027-02-01", "2027-02-01")]   // un mes futuro: hasta ese mes
    [InlineData("2031-01-01", "2028-10-01")]   // más de 24 meses: tope
    public void Genera_hasta_el_mes_que_se_mira(string visto, string esperado)
    {
        Assert.Equal(DateOnly.Parse(esperado), RecurrenteService.HastaQueMes(Oct, DateOnly.Parse(visto)));
        Assert.Equal(Oct, RecurrenteService.HastaQueMes(Oct, null));
    }

    [Fact]
    public async Task Al_mirar_febrero_ya_estan_los_fijos_de_febrero()
    {
        await using var db = Contexto(Ana);
        var s = new RecurrenteService(db);
        var r = await s.CrearAsync(Fijo("Facultad", 9000m, Oct), Oct);

        var feb = new DateOnly(2027, 2, 1);
        Assert.Equal(4, await s.GenerarPendientesAsync(feb));      // noviembre a febrero
        Assert.Equal(0, await s.GenerarPendientesAsync(feb));      // sin duplicar
        Assert.Equal(0, await s.GenerarPendientesAsync(Oct));      // volver a un mes anterior no borra nada
        Assert.Equal(5, (await GastosDeAsync(Ana, r.Id)).Count);
        Assert.Equal(9000m, (await new GastoService(db).ResumenAsync(feb)).Total(Moneda.UYU));
        Assert.Empty(await s.PrevistosAsync(feb));
    }

    [Fact]
    public async Task Editar_rehace_los_meses_futuros_ya_cargados_salvo_los_ajustados_a_mano()
    {
        int id;
        await using (var db = Contexto(Ana))
        {
            var s = new RecurrenteService(db);
            id = (await s.CrearAsync(Fijo("Facultad", 9000m, Oct), Oct)).Id;
            await s.GenerarPendientesAsync(new DateOnly(2027, 1, 1));   // octubre a enero
        }
        // Diciembre se ajustó a mano.
        var dic = (await GastosDeAsync(Ana, id)).Single(g => g.PrimerPeriodo == new DateOnly(2026, 12, 1));
        await using (var db = Contexto(Ana))
            await new GastoService(db).ActualizarAsync(dic.Id, new GastoInput("Facultad", null, dic.Fecha, null, null, Moneda.UYU, 4500m, 1, 1, "media beca"));

        await using (var db = Contexto(Ana))
        {
            var s = new RecurrenteService(db);
            Assert.True(await s.ActualizarAsync(id, Fijo("Facultad", 9500m, Oct), Oct));
            await s.GenerarPendientesAsync(new DateOnly(2027, 1, 1));
        }

        var montos = (await GastosDeAsync(Ana, id)).ToDictionary(g => g.PrimerPeriodo.Month, g => g.MontoCuota);
        Assert.Equal(9000m, montos[10]);   // el mes actual no cambia
        Assert.Equal(9500m, montos[11]);   // los futuros toman el valor nuevo
        Assert.Equal(4500m, montos[12]);   // el ajustado a mano se respeta
        Assert.Equal(9500m, montos[1]);
        Assert.Equal(4, montos.Count);
    }

    [Fact]
    public async Task Pausar_y_eliminar_quitan_los_meses_futuros()
    {
        await using var db = Contexto(Ana);
        var s = new RecurrenteService(db);
        var pausa = await s.CrearAsync(Fijo("Gimnasio", 1500m, Oct), Oct);
        var borrar = await s.CrearAsync(Fijo("Netflix", 400m, Oct), Oct);
        await s.GenerarPendientesAsync(new DateOnly(2027, 2, 1));

        await s.CambiarEstadoAsync(pausa.Id, Oct);
        Assert.Equal(new[] { Oct }, (await GastosDeAsync(Ana, pausa.Id)).Select(g => g.PrimerPeriodo));

        Assert.Equal(1, await s.EliminarAsync(borrar.Id, Oct));    // queda octubre, como gasto común
        Assert.Equal(1, await db.Gastos.CountAsync(g => g.Descripcion == "Netflix"));
    }

    [Fact]
    public async Task Previstos_y_proyeccion_incluyen_meses_futuros()
    {
        await using var db = Contexto(Ana);
        await new RecurrenteService(db).CrearAsync(Fijo("Facultad", 9000m, Oct), Oct);

        Assert.Empty(await new RecurrenteService(db).PrevistosAsync(Oct)); // octubre ya está cargado
        Assert.Single(await new RecurrenteService(db).PrevistosAsync(Oct.AddMonths(1)));

        var proy = await new GastoService(db).ProyeccionAsync(Oct, 3);
        Assert.Equal(new[] { 9000m, 9000m, 9000m }, proy.Select(p => p.UYU));
        Assert.Equal(new[] { 0m, 9000m, 9000m }, proy.Select(p => p.PrevistoUYU));
    }

    [Fact]
    public async Task Otro_usuario_no_ve_ni_genera_los_fijos_ajenos()
    {
        await using (var db = Contexto(Ana))
            await new RecurrenteService(db).CrearAsync(Fijo("Facultad", 9000m, Oct), Oct);

        await using var dbBeto = Contexto(Beto);
        var s = new RecurrenteService(dbBeto);
        Assert.Equal(0, await s.GenerarPendientesAsync(Oct.AddMonths(3)));
        Assert.Empty(await dbBeto.GastosRecurrentes.ToListAsync());
        Assert.Empty(await s.PrevistosAsync(Oct.AddMonths(1)));

        await using var dbAna = Contexto(Ana);
        Assert.Single(await dbAna.Gastos.ToListAsync()); // Beto no generó meses de Ana
    }

    [Fact]
    public async Task Valida_mes_final_anterior_al_inicial()
    {
        await using var db = Contexto(Ana);
        await Assert.ThrowsAsync<ValidacionException>(() =>
            new RecurrenteService(db).CrearAsync(Fijo("X", 1m, Oct, hasta: Oct.AddMonths(-1)), Oct));
    }

    // ---------- Día de cierre ----------

    [Theory]
    [InlineData(25, 10, 25, 10)] // el día del cierre todavía entra en el mes
    [InlineData(25, 10, 26, 11)] // después del cierre pasa al mes siguiente
    [InlineData(31, 2, 28, 2)]   // cierre 31 en febrero = último día del mes
    public void Periodo_de_facturacion_con_cierre(int cierre, int mes, int dia, int mesEsperado)
    {
        var p = PlanDeCuotas.PeriodoDeFacturacion(new DateOnly(2027, mes, dia), cierre);
        Assert.Equal(mesEsperado, p.Month);
    }

    [Fact]
    public async Task Compra_despues_del_cierre_empieza_el_mes_siguiente()
    {
        await using var db = Contexto(Ana);
        var visa = new Tarjeta { Nombre = "Visa", Tipo = TipoTarjeta.Credito, DiaCierre = 25 };
        var debito = new Tarjeta { Nombre = "Débito", Tipo = TipoTarjeta.Debito, DiaCierre = 25 }; // en débito se ignora
        db.Tarjetas.AddRange(visa, debito);
        await db.SaveChangesAsync();
        var s = new GastoService(db);
        var fecha = new DateOnly(2026, 10, 28);

        var credito = await s.CrearAsync(new GastoInput("Heladera", null, fecha, visa.Id, null, Moneda.UYU, 1000m, 3, 1, null));
        var conDebito = await s.CrearAsync(new GastoInput("Súper", null, fecha, debito.Id, null, Moneda.UYU, 500m, 1, 1, null));

        Assert.Equal(new DateOnly(2026, 11, 1), credito.PrimerPeriodo);
        Assert.Equal(Oct, conDebito.PrimerPeriodo);
    }

    // ---------- Planes activos ----------

    [Fact]
    public async Task Planes_activos_calcula_lo_que_falta_pagar()
    {
        await using var db = Contexto(Ana);
        var s = new GastoService(db);
        await s.CrearAsync(new GastoInput("Nueva Visión", null, new DateOnly(2026, 10, 6), null, null, Moneda.UYU, 1830m, 6, 5, null)); // 5/6
        await s.CrearAsync(new GastoInput("Thot", null, new DateOnly(2026, 9, 9), null, null, Moneda.USD, 22m, 6, 6, null));          // terminó en sept.
        await s.CrearAsync(new GastoInput("Contado", null, new DateOnly(2026, 10, 1), null, null, Moneda.UYU, 100m, 1, 1, null));

        var planes = await s.PlanesActivosAsync(Oct);
        var p = Assert.Single(planes);
        Assert.Equal(5, p.CuotaEsteMes);
        Assert.Equal(2, p.Faltan);           // octubre y noviembre
        Assert.Equal(3660m, p.Saldo);
        Assert.Equal(4, p.Pagadas);
    }
}
