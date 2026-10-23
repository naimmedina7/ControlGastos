using ControlGastos.Web.Domain;

namespace ControlGastos.Tests;

public static class Muestras
{
    /// <summary>El estado de cuenta de octubre 2026 tal como lo pegó el usuario (separado por tabuladores).</summary>
    public const string Octubre =
        "**** 1012\tMERPAGO*MERCADOLIBRE\tPlan Pagos\t05/10/26\tPesos\t407,90\t3/3\n" +
        "**** 1012\tNUEVA VISION\tPlan Pagos\t06/10/26\tPesos\t1.830,00\t5/6\n" +
        "**** 1012\tRECIBO DE PAGO\t\t07/10/26\tDólares\t-121,43\t\n" +
        "**** 1012\tRECIBO DE PAGO\t\t07/10/26\tPesos\t-15.189,15\t\n" +
        "**** 1012\tMERPAGO*SEP\tPlan Pagos\t08/10/26\tPesos\t409,37\t1/10\n" +
        "**** 1012\tMERPAGO*LAOFERTAIRRE\tPlan Pagos\t08/10/26\tPesos\t2.702,42\t9/10\n" +
        "**** 1012\tTHOT COMPUTACION\tPlan Pagos\t09/10/26\tDólares\t22,00\t6/6\n" +
        "**** 1012\tWORKANA LLC\tComun\t10/10/26\tDólares\t10,27\t\n" +
        "**** 1012\tMERPAGO*SAMPLER\tPlan Pagos\t10/10/26\tPesos\t633,21\t5/6\n" +
        "**** 1012\tDLO*PedidosYa Burger\tComun\t11/10/26\tPesos\t1.398,61\t\n" +
        "**** 1012\tDLO*PedidosYa Envio\tComun\t11/10/26\tPesos\t33,44\t\n" +
        "**** 1012\tDLO*PedidosYa Propin\tComun\t12/10/26\tPesos\t40,00\t\n" +
        "**** 1012\tANTEL EN LINEA\tComun\t13/10/26\tPesos\t65,00\t\n" +
        "**** 1012\tMERPAGO*MERCADOLIBRE\tPlan Pagos\t13/10/26\tPesos\t877,00\t1/6\n" +
        "**** 1012\tMERPAGO*VICTOR\tPlan Pagos\t14/10/26\tPesos\t549,00\t2/10\n" +
        "**** 1012\tMERPAGO*MERCADOLIBRE\tPlan Pagos\t14/10/26\tPesos\t664,60\t1/5\n" +
        "**** 1012\tDISA LOS PINOS\tComun\t16/10/26\tPesos\t500,00\t\n" +
        "**** 1012\tMERPAGO*DAGHEROS\tPlan Pagos\t17/10/26\tPesos\t385,93\t2/3\n" +
        "**** 1012\tMERPAGO*TKTX\tComun\t19/10/26\tPesos\t740,00\t\n" +
        "**** 1012\tMERPAGO*LACUEVAMUEBL\tPlan Pagos\t21/10/26\tPesos\t998,33\t1/6\n" +
        "**** 1012\tNETFLIX.COM\tComun\t21/10/26\tDólares\t5,17\t\n" +
        "**** 1012\tDLO*Tiendamia UY - C\tPlan Pagos\t25/10/26\tPesos\t1.215,90\t2/12\n" +
        "**** 1012\tMERPAGO*FINISH\tPlan Pagos\t25/10/26\tPesos\t482,77\t3/10\n";
}

public class EstadoDeCuentaTests
{
    [Fact]
    public void Interpreta_el_estado_de_cuenta_real_completo()
    {
        var lineas = EstadoDeCuenta.Interpretar(Muestras.Octubre);

        Assert.Equal(23, lineas.Count);
        Assert.Equal(21, lineas.Count(l => l.Tipo == TipoLinea.Gasto));
        Assert.Equal(2, lineas.Count(l => l.Tipo == TipoLinea.PagoTarjeta));
        Assert.DoesNotContain(lineas, l => l.Tipo == TipoLinea.NoReconocida);

        var gastos = lineas.Where(l => l.Tipo == TipoLinea.Gasto).ToList();
        Assert.Equal(13933.48m, gastos.Where(l => l.Moneda == Moneda.UYU).Sum(l => l.Monto));
        Assert.Equal(37.44m, gastos.Where(l => l.Moneda == Moneda.USD).Sum(l => l.Monto));
    }

    [Fact]
    public void Lee_comercio_fecha_moneda_monto_y_cuotas()
    {
        var l = EstadoDeCuenta.InterpretarLinea(1, "**** 1012\tNUEVA VISION\tPlan Pagos\t06/10/26\tPesos\t1.830,00\t5/6");
        Assert.Equal(TipoLinea.Gasto, l.Tipo);
        Assert.Equal("NUEVA VISION", l.Comercio);
        Assert.Equal(new DateOnly(2026, 10, 6), l.Fecha);
        Assert.Equal(Moneda.UYU, l.Moneda);
        Assert.Equal(1830m, l.Monto);
        Assert.Equal((5, 6), (l.CuotaActual, l.CantidadCuotas));

        var usd = EstadoDeCuenta.InterpretarLinea(2, "**** 1012\tWORKANA LLC\tComun\t10/10/26\tDólares\t10,27\t");
        Assert.Equal(Moneda.USD, usd.Moneda);
        Assert.Equal(10.27m, usd.Monto);
        Assert.False(usd.EnCuotas);
    }

    [Fact]
    public void Funciona_con_espacios_en_lugar_de_tabuladores_y_sin_mascara()
    {
        var l = EstadoDeCuenta.InterpretarLinea(1, "DLO*Tiendamia UY - C   Plan Pagos   25/10/2026   Pesos   1.215,90   2/12");
        Assert.Equal(TipoLinea.Gasto, l.Tipo);
        Assert.Equal("DLO*Tiendamia UY - C", l.Comercio);
        Assert.Equal(new DateOnly(2026, 10, 25), l.Fecha);
        Assert.Equal((2, 12), (l.CuotaActual, l.CantidadCuotas));
    }

    [Fact]
    public void Los_recibos_de_pago_y_montos_negativos_no_son_gastos()
    {
        Assert.Equal(TipoLinea.PagoTarjeta, EstadoDeCuenta.InterpretarLinea(1, "**** 1012\tRECIBO DE PAGO\t\t07/10/26\tPesos\t-15.189,15\t").Tipo);
        Assert.Equal(TipoLinea.PagoTarjeta, EstadoDeCuenta.InterpretarLinea(2, "DEVOLUCION COMPRA 07/10/26 Pesos -500,00").Tipo);
    }

    [Theory]
    [InlineData("Movimientos del mes")]                                   // encabezado
    [InlineData("**** 1012\tCOMERCIO\tComun\t32/10/26\tPesos\t10,00")]   // fecha imposible
    [InlineData("**** 1012\tCOMERCIO\tPlan Pagos\t05/10/26\tPesos\t10,00\t7/6")] // cuota mayor al total
    [InlineData("TOTAL A PAGAR 15.189,15")]
    public void Marca_como_no_reconocidas_las_lineas_que_no_son_movimientos(string linea)
    {
        Assert.Equal(TipoLinea.NoReconocida, EstadoDeCuenta.InterpretarLinea(1, linea).Tipo);
    }

    [Theory]
    [InlineData("1.830,00", 1830)]
    [InlineData("407,90", 407.9)]
    [InlineData("-15.189,15", -15189.15)]
    [InlineData("1830.00", 1830)]
    [InlineData("1.830", 1830)]
    public void Interpreta_montos_en_formato_uruguayo(string texto, double esperado)
    {
        Assert.True(EstadoDeCuenta.TryMonto(texto, out var m));
        Assert.Equal((decimal)esperado, m);
    }

    [Theory]
    [InlineData("MERPAGO*LACUEVAMUEBL", "Lacuevamuebl")]
    [InlineData("NETFLIX.COM", "Netflix")]
    [InlineData("DLO*PedidosYa Burger", "Pedidosya Burger")]
    [InlineData("ANTEL EN LINEA", "Antel En Linea")]
    public void Sugiere_una_descripcion_legible(string comercio, string esperado)
    {
        Assert.Equal(esperado, EstadoDeCuenta.DescripcionSugerida(comercio));
    }

    [Fact]
    public void Normaliza_comercios_para_compararlos()
    {
        Assert.Equal(EstadoDeCuenta.Normalizar("  merpago*Mercadolibre "), EstadoDeCuenta.Normalizar("MERPAGO*MERCADOLIBRE"));
        Assert.Equal("NUEVA VISION", EstadoDeCuenta.Normalizar("Nueva   Visión"));
    }

    [Fact]
    public void Limita_la_cantidad_de_lineas()
    {
        string texto = string.Concat(Enumerable.Repeat("COMERCIO Comun 05/10/26 Pesos 10,00\n", 1000));
        Assert.True(EstadoDeCuenta.Interpretar(texto).Count <= EstadoDeCuenta.MaxLineas);
    }
}
