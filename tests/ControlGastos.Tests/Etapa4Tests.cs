using System.Text;
using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Infrastructure;
using ControlGastos.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Tests;

/// <summary>Importación, reportes, tipo de cambio, presupuestos y CSV, sobre SQLite en memoria.</summary>
public class Etapa4Tests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private const string Ana = "user-ana";
    private const string Beto = "user-beto";
    private static readonly DateOnly Oct = new(2026, 10, 1);
    private static readonly DateOnly Nov = new(2026, 11, 1);

    public Etapa4Tests()
    {
        _conn.Open();
        using var db = Contexto(null);
        db.Database.EnsureCreated();
        db.Users.AddRange(
            new AppUser { Id = Ana, UserName = "ana@test", Email = "ana@test" },
            new AppUser { Id = Beto, UserName = "beto@test", Email = "beto@test" });
        db.Categorias.AddRange(CategoriasPorDefecto.Crear(Ana));
        db.Categorias.AddRange(CategoriasPorDefecto.Crear(Beto));
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    private SqliteAppDbContext Contexto(string? userId) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options, new UsuarioFijo(userId));

    private static ImportacionService Importacion(AppDbContext db) => new(db, new GastoService(db));

    private async Task<int> TarjetaAsync(AppDbContext db, int? cierre = null)
    {
        var t = new Tarjeta { Nombre = "Visa " + Guid.NewGuid().ToString("N")[..6], Tipo = TipoTarjeta.Credito, DiaCierre = cierre };
        db.Tarjetas.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static List<LineaAImportar> Seleccion(AnalisisImportacion a) =>
        a.Items.Where(i => i.Incluir)
            .Select(i => new LineaAImportar(i.Linea.Comercio, i.Linea.Fecha, i.Linea.Moneda, i.Linea.Monto, i.Linea.CuotaActual, i.Linea.CantidadCuotas, i.Descripcion, i.CategoriaId))
            .ToList();

    // ---------------- Importación ----------------

    [Fact]
    public async Task Importar_el_estado_de_cuenta_real_da_los_mismos_totales()
    {
        await using var db = Contexto(Ana);
        int visa = await TarjetaAsync(db);
        var imp = Importacion(db);

        var analisis = await imp.AnalizarAsync(Muestras.Octubre, visa, null);
        Assert.Equal(Oct, analisis.MesEstado);
        Assert.Equal(21, analisis.Nuevos);
        Assert.Equal(2, analisis.Pagos);

        Assert.Equal(21, await imp.ImportarAsync(visa, analisis.MesEstado, Seleccion(analisis)));

        var r = await new GastoService(db).ResumenAsync(Oct);
        Assert.Equal(13933.48m, r.Total(Moneda.UYU));
        Assert.Equal(37.44m, r.Total(Moneda.USD));

        var nv = await db.Gastos.SingleAsync(g => g.Comercio == "NUEVA VISION");
        Assert.Equal(new DateOnly(2026, 6, 1), nv.PrimerPeriodo); // 5/6 en octubre
        Assert.Equal(visa, nv.TarjetaId);
    }

    [Fact]
    public async Task Propone_categorias_por_reglas()
    {
        await using var db = Contexto(Ana);
        var a = await Importacion(db).AnalizarAsync(Muestras.Octubre, null, null);
        var cats = await db.Categorias.ToDictionaryAsync(c => c.Id, c => c.Nombre);
        string Cat(string comercio) => cats[a.Gastos.First(i => i.Linea.Comercio == comercio).CategoriaId!.Value];

        Assert.Equal("Delivery y comidas", Cat("DLO*PedidosYa Burger"));
        Assert.Equal("Suscripciones", Cat("NETFLIX.COM"));
        Assert.Equal("Servicios", Cat("ANTEL EN LINEA"));
        Assert.Equal("Transporte y combustible", Cat("DISA LOS PINOS"));
        Assert.Equal("Compras", Cat("MERPAGO*MERCADOLIBRE"));
        Assert.Equal("Entretenimiento", Cat("MERPAGO*TKTX"));
        Assert.Equal("Salud", Cat("NUEVA VISION"));
        Assert.Equal("Trabajo", Cat("WORKANA LLC"));
        Assert.Null(a.Gastos.First(i => i.Linea.Comercio == "MERPAGO*SEP").CategoriaId);
    }

    [Fact]
    public async Task Volver_a_importar_lo_mismo_marca_todo_como_duplicado()
    {
        await using var db = Contexto(Ana);
        int visa = await TarjetaAsync(db);
        var imp = Importacion(db);
        var a1 = await imp.AnalizarAsync(Muestras.Octubre, visa, null);
        await imp.ImportarAsync(visa, a1.MesEstado, Seleccion(a1));

        var a2 = await imp.AnalizarAsync(Muestras.Octubre, visa, null);
        Assert.Equal(0, a2.Nuevos);
        Assert.Equal(21, a2.Duplicados);
        Assert.All(a2.Gastos, i => Assert.False(i.Incluir));
    }

    [Fact]
    public async Task El_estado_de_cuenta_siguiente_no_duplica_las_cuotas_de_planes_ya_cargados()
    {
        await using var db = Contexto(Ana);
        int visa = await TarjetaAsync(db);
        var imp = Importacion(db);
        var a1 = await imp.AnalizarAsync(Muestras.Octubre, visa, null);
        await imp.ImportarAsync(visa, a1.MesEstado, Seleccion(a1));

        string noviembre =
            "**** 1012\tNUEVA VISION\tPlan Pagos\t06/11/26\tPesos\t1.830,00\t6/6\n" +          // sigue el plan
            "**** 1012\tMERPAGO*LACUEVAMUEBL\tPlan Pagos\t21/11/26\tPesos\t998,33\t2/6\n" +   // sigue el plan
            "**** 1012\tDLO*PedidosYa Burger\tComun\t03/11/26\tPesos\t950,00\t\n";           // compra nueva
        var a2 = await imp.AnalizarAsync(noviembre, visa, null);

        Assert.Equal(Nov, a2.MesEstado);
        Assert.True(a2.Gastos.Single(i => i.Linea.Comercio == "NUEVA VISION").Duplicado);
        Assert.True(a2.Gastos.Single(i => i.Linea.Comercio == "MERPAGO*LACUEVAMUEBL").Duplicado);
        Assert.False(a2.Gastos.Single(i => i.Linea.Comercio == "DLO*PedidosYa Burger").Duplicado);
    }

    [Fact]
    public async Task Aprende_descripcion_y_categoria_de_lo_que_el_usuario_corrigio()
    {
        await using var db = Contexto(Ana);
        int visa = await TarjetaAsync(db);
        var imp = Importacion(db);
        var a1 = await imp.AnalizarAsync(Muestras.Octubre, visa, null);
        int hogar = (await db.Categorias.SingleAsync(c => c.Nombre == "Hogar")).Id;
        var sel = Seleccion(a1).Select(l => l.Comercio == "MERPAGO*SEP" ? l with { Descripcion = "Sillas escritorio", CategoriaId = hogar } : l).ToList();
        await imp.ImportarAsync(visa, a1.MesEstado, sel);

        var a2 = await imp.AnalizarAsync("**** 1012\tMERPAGO*SEP\tComun\t02/11/26\tPesos\t120,00\t", visa, null);
        var item = a2.Gastos.Single();
        Assert.Equal("Sillas escritorio", item.Descripcion);
        Assert.Equal(hogar, item.CategoriaId);
        Assert.Equal("historial", item.OrigenCategoria);
    }

    [Fact]
    public async Task El_mes_del_estado_de_cuenta_manda_sobre_el_dia_de_cierre()
    {
        await using var db = Contexto(Ana);
        int visa = await TarjetaAsync(db, cierre: 20);
        var imp = Importacion(db);
        // Compra del 25/10 con cierre el 20: a mano iría a noviembre, pero el usuario la importa del estado de octubre.
        var a = await imp.AnalizarAsync("**** 1012\tMERPAGO*FINISH\tComun\t25/10/26\tPesos\t100,00\t", visa, Oct);
        await imp.ImportarAsync(visa, a.MesEstado, Seleccion(a));
        Assert.Equal(Oct, (await db.Gastos.SingleAsync()).PrimerPeriodo);
    }

    [Fact]
    public async Task Si_una_linea_falla_no_se_importa_ninguna()
    {
        await using var db = Contexto(Ana);
        int visa = await TarjetaAsync(db);
        int categoriaDeBeto;
        await using (var dbBeto = Contexto(Beto)) categoriaDeBeto = (await dbBeto.Categorias.FirstAsync()).Id;

        var lineas = new List<LineaAImportar>
        {
            new("COMERCIO A", new DateOnly(2026, 10, 1), Moneda.UYU, 10m, 1, 1, "A", null),
            new("COMERCIO B", new DateOnly(2026, 10, 2), Moneda.UYU, 20m, 1, 1, "B", categoriaDeBeto), // categoría ajena
        };
        await Assert.ThrowsAsync<ValidacionException>(() => Importacion(db).ImportarAsync(visa, Oct, lineas));

        await using var db2 = Contexto(Ana);
        Assert.Empty(await db2.Gastos.ToListAsync());
    }

    [Fact]
    public async Task El_historial_y_los_duplicados_son_por_usuario()
    {
        await using (var db = Contexto(Ana))
        {
            int visa = await TarjetaAsync(db);
            var a = await Importacion(db).AnalizarAsync(Muestras.Octubre, visa, null);
            await Importacion(db).ImportarAsync(visa, a.MesEstado, Seleccion(a));
        }
        await using var dbBeto = Contexto(Beto);
        var aBeto = await Importacion(dbBeto).AnalizarAsync(Muestras.Octubre, null, null);
        Assert.Equal(21, aBeto.Nuevos); // lo de Ana no cuenta como duplicado para Beto
    }

    // ---------------- Reportes y tipo de cambio ----------------

    private static async Task CargarAsync(AppDbContext db)
    {
        var s = new GastoService(db);
        await s.CrearAsync(new GastoInput("Súper", null, new DateOnly(2026, 9, 5), null, null, Moneda.UYU, 1000m, 1, 1, null));
        await s.CrearAsync(new GastoInput("Heladera", null, new DateOnly(2026, 9, 10), null, null, Moneda.UYU, 500m, 3, 1, null)); // sep, oct, nov
        await s.CrearAsync(new GastoInput("Netflix", null, new DateOnly(2026, 10, 21), null, null, Moneda.USD, 10m, 1, 1, null));
        await new RecurrenteService(db).CrearAsync(new RecurrenteInput("Facultad", null, null, Moneda.UYU, 9000m, 1, Oct, null, null), Oct);
    }

    [Fact]
    public async Task El_reporte_separa_contado_cuotas_y_fijos_y_proyecta_el_futuro()
    {
        await using var db = Contexto(Ana);
        await CargarAsync(db);
        var r = await new ReporteService(db, new GastoService(db)).GenerarAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), Oct, VistaMoneda.UYU);

        Assert.Equal(4, r.Meses.Count);
        var sep = r.Meses[0]; var oct = r.Meses[1]; var nov = r.Meses[2]; var dic = r.Meses[3];
        Assert.Equal((1000m, 500m, 0m), (sep.Contado, sep.Cuotas, sep.Fijos));
        Assert.Equal((0m, 500m, 9000m), (oct.Contado, oct.Cuotas, oct.Fijos));
        Assert.True(nov.Proyectado);
        Assert.Equal((500m, 9000m), (nov.Cuotas, nov.Fijos));   // última cuota + fijo previsto
        Assert.Equal(9000m, dic.Total);

        Assert.Equal(11000m, r.Total);          // solo meses transcurridos: sep + oct
        Assert.Equal(5500m, r.Promedio);
        Assert.Equal(Oct, r.MesMaximo!.Mes);
    }

    [Fact]
    public async Task Vista_en_dolares_y_pesificada()
    {
        await using var db = Contexto(Ana);
        await CargarAsync(db);
        var rs = new ReporteService(db, new GastoService(db));

        var usd = await rs.GenerarAsync(Oct, Oct, Oct, VistaMoneda.USD);
        Assert.Equal(10m, usd.Total);

        var sinTc = await rs.GenerarAsync(Oct, Oct, Oct, VistaMoneda.Pesificado);
        Assert.Equal(9500m, sinTc.Total);                     // los dólares quedan afuera
        Assert.Equal(new[] { Oct }, sinTc.MesesSinTipoCambio);

        db.TiposCambio.Add(new TipoCambio { Mes = new DateOnly(2026, 8, 1), UsdEnPesos = 40m }); // se usa la del último mes anterior
        await db.SaveChangesAsync();
        var conTc = await rs.GenerarAsync(Oct, Oct, Oct, VistaMoneda.Pesificado);
        Assert.Equal(9900m, conTc.Total);
        Assert.Empty(conTc.MesesSinTipoCambio);
    }

    [Fact]
    public void La_tabla_de_cotizaciones_usa_la_ultima_anterior()
    {
        var t = new TablaTiposCambio(new[]
        {
            new TipoCambio { Mes = new DateOnly(2026, 8, 1), UsdEnPesos = 40m },
            new TipoCambio { Mes = Oct, UsdEnPesos = 41.5m },
        });
        Assert.Null(t.Para(new DateOnly(2026, 7, 1)));
        Assert.Equal(40m, t.Para(new DateOnly(2026, 9, 1)));
        Assert.Equal(41.5m, t.Para(new DateOnly(2027, 1, 1)));
        Assert.Equal(83m, t.Convertir(2m, Moneda.USD, Oct, VistaMoneda.Pesificado));
        Assert.Null(t.Convertir(2m, Moneda.USD, Oct, VistaMoneda.UYU));
    }

    [Fact]
    public async Task Por_categoria_cuenta_solo_meses_transcurridos()
    {
        await using var db = Contexto(Ana);
        await CargarAsync(db);
        var r = await new ReporteService(db, new GastoService(db)).GenerarAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), Oct, VistaMoneda.UYU);
        Assert.Equal(r.Total, r.PorCategoria.Sum(c => c.Total));
        Assert.Equal(r.Total, r.PorTarjeta.Sum(c => c.Total));
    }

    [Fact]
    public async Task Otro_usuario_ve_su_reporte_vacio()
    {
        await using (var db = Contexto(Ana)) await CargarAsync(db);
        await using var dbBeto = Contexto(Beto);
        var r = await new ReporteService(dbBeto, new GastoService(dbBeto)).GenerarAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), Oct, VistaMoneda.UYU);
        Assert.Equal(0m, r.Meses.Sum(m => m.Total));
        Assert.Empty(r.PorCategoria);
    }

    // ---------------- Presupuestos ----------------

    [Fact]
    public async Task Presupuesto_calcula_avance_y_nivel()
    {
        await using var db = Contexto(Ana);
        var cats = await db.Categorias.ToDictionaryAsync(c => c.Nombre, c => c.Id);
        var s = new GastoService(db);
        await s.CrearAsync(new GastoInput("Burger", null, new DateOnly(2026, 10, 11), null, cats["Delivery y comidas"], Moneda.UYU, 1398.61m, 1, 1, null));
        await s.CrearAsync(new GastoInput("Envío", null, new DateOnly(2026, 10, 11), null, cats["Delivery y comidas"], Moneda.UYU, 33.44m, 1, 1, null));
        await s.CrearAsync(new GastoInput("Antel", null, new DateOnly(2026, 10, 13), null, cats["Servicios"], Moneda.UYU, 65m, 1, 1, null));

        var ps = new PresupuestoService(db);
        await ps.GuardarAsync(cats["Delivery y comidas"], Moneda.UYU, 1500m); // 95,5 % → cerca
        await ps.GuardarAsync(cats["Servicios"], Moneda.UYU, 50m);            // 130 % → excedido
        await ps.GuardarAsync(cats["Salud"], Moneda.UYU, 1000m);              // 0 % → ok
        await ps.GuardarAsync(cats["Salud"], Moneda.UYU, 2000m);              // actualiza, no duplica

        var estado = await ps.EstadoAsync(Oct);
        Assert.Equal(3, estado.Count);
        Assert.Equal(NivelPresupuesto.Excedido, estado.Single(e => e.Presupuesto.CategoriaId == cats["Servicios"]).Nivel);
        var delivery = estado.Single(e => e.Presupuesto.CategoriaId == cats["Delivery y comidas"]);
        Assert.Equal(NivelPresupuesto.Cerca, delivery.Nivel);
        Assert.Equal(1432.05m, delivery.Gastado);
        Assert.Equal(2000m, estado.Single(e => e.Presupuesto.CategoriaId == cats["Salud"]).Presupuesto.MontoMensual);
    }

    [Fact]
    public async Task No_se_puede_presupuestar_una_categoria_ajena()
    {
        int categoriaDeBeto;
        await using (var dbBeto = Contexto(Beto)) categoriaDeBeto = (await dbBeto.Categorias.FirstAsync()).Id;
        await using var db = Contexto(Ana);
        await Assert.ThrowsAsync<ValidacionException>(() => new PresupuestoService(db).GuardarAsync(categoriaDeBeto, Moneda.UYU, 100m));
    }

    // ---------------- CSV ----------------

    [Theory]
    [InlineData("Normal", "Normal")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+5", "'+5")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("con; punto y coma", "\"con; punto y coma\"")]
    public void Escapa_campos_y_evita_inyeccion_de_formulas(string valor, string esperado)
    {
        Assert.Equal(esperado, ExportadorCsv.Campo(valor));
    }

    [Fact]
    public async Task Exporta_una_fila_por_cuota_con_coma_decimal_y_BOM()
    {
        await using var db = Contexto(Ana);
        await CargarAsync(db);
        var filas = await new ReporteService(db, new GastoService(db)).MovimientosAsync(Oct, Oct);
        var bytes = ExportadorCsv.Generar(filas);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var lineas = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).TrimEnd().Split('\n');
        Assert.StartsWith("Mes;Fecha;Descripción", lineas[0]);
        Assert.Equal(4, lineas.Length); // encabezado + heladera 2/3 + netflix + facultad
        Assert.Contains(lineas, l => l.Contains("Heladera") && l.Contains(";Cuotas;2/3;UYU;500,00"));
        Assert.Contains(lineas, l => l.Contains("Facultad") && l.Contains(";Fijo;"));
    }
}
