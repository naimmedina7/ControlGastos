using ControlGastos.Web.Domain;

namespace ControlGastos.Tests;

public class PlanDeCuotasTests
{
    [Fact]
    public void Compra_ya_empezada_calcula_el_mes_de_la_primera_cuota()
    {
        // "NUEVA VISION 5/6" en octubre 2026 → la cuota 1 fue en junio.
        var primero = PlanDeCuotas.PrimerPeriodo(new DateOnly(2026, 10, 6), cuotaActual: 5);
        Assert.Equal(new DateOnly(2026, 6, 1), primero);
    }

    [Fact]
    public void Genera_una_cuota_por_mes_hasta_la_ultima()
    {
        var gasto = new Gasto { MontoCuota = 1830m, CantidadCuotas = 6, PrimerPeriodo = new DateOnly(2026, 6, 1) };
        var cuotas = PlanDeCuotas.Generar(gasto);

        Assert.Equal(6, cuotas.Count);
        Assert.Equal(Enumerable.Range(1, 6), cuotas.Select(c => c.Numero));
        Assert.Equal(new DateOnly(2026, 6, 1), cuotas[0].Periodo);
        Assert.Equal(new DateOnly(2026, 11, 1), cuotas[^1].Periodo);
        Assert.All(cuotas, c => Assert.Equal(1830m, c.Monto));
        Assert.Equal(10980m, gasto.MontoTotal);
    }

    [Fact]
    public void El_plan_cruza_el_cambio_de_anio()
    {
        // "Tiendamia 2/12" en octubre 2026 → de septiembre 2026 a agosto 2027.
        var gasto = new Gasto { MontoCuota = 1215.90m, CantidadCuotas = 12, PrimerPeriodo = PlanDeCuotas.PrimerPeriodo(new DateOnly(2026, 10, 25), 2) };
        var cuotas = PlanDeCuotas.Generar(gasto);

        Assert.Equal(new DateOnly(2026, 9, 1), cuotas[0].Periodo);
        Assert.Equal(new DateOnly(2027, 8, 1), cuotas[^1].Periodo);
        Assert.Equal(new DateOnly(2027, 8, 1), gasto.UltimoPeriodo);
    }

    [Theory]
    [InlineData(2026, 5, 0)]   // antes del plan
    [InlineData(2026, 6, 1)]
    [InlineData(2026, 10, 5)]
    [InlineData(2026, 11, 6)]
    [InlineData(2026, 12, 0)]  // después del plan
    public void Numero_de_cuota_en_un_mes(int anio, int mes, int esperado)
    {
        int n = PlanDeCuotas.NumeroEnPeriodo(new DateOnly(2026, 6, 1), 6, new DateOnly(anio, mes, 15));
        Assert.Equal(esperado, n);
    }

    [Fact]
    public void Contado_es_un_plan_de_una_cuota_en_su_mes()
    {
        var gasto = new Gasto { MontoCuota = 65m, CantidadCuotas = 1, PrimerPeriodo = PlanDeCuotas.PrimerPeriodo(new DateOnly(2026, 10, 13), 1) };
        var c = Assert.Single(PlanDeCuotas.Generar(gasto));
        Assert.Equal(new DateOnly(2026, 10, 1), c.Periodo);
    }

    [Theory]
    [InlineData(0, 1, 1)]      // monto cero
    [InlineData(-5, 1, 1)]     // monto negativo
    [InlineData(10, 0, 1)]     // sin cuotas
    [InlineData(10, 73, 1)]    // demasiadas cuotas
    [InlineData(10, 6, 7)]     // cuota actual mayor que el total
    [InlineData(10, 6, 0)]     // cuota actual cero
    public void Rechaza_datos_invalidos(decimal monto, int cantidad, int actual)
    {
        Assert.Throws<ArgumentException>(() => PlanDeCuotas.Validar(monto, cantidad, actual));
    }
}
