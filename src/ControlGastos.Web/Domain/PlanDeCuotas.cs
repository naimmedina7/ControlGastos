namespace ControlGastos.Web.Domain;

/// <summary>Cálculo de períodos de un plan de cuotas. Sin dependencias, para poder testearlo aislado.</summary>
public static class PlanDeCuotas
{
    public const int MaxCuotas = 72;

    /// <summary>Primer día del mes de la fecha.</summary>
    public static DateOnly Periodo(DateOnly fecha) => new(fecha.Year, fecha.Month, 1);

    /// <summary>
    /// Mes del estado de cuenta en que entra una compra. Sin día de cierre es el mes de la compra;
    /// con cierre, una compra posterior a ese día pasa al mes siguiente.
    /// Ej.: cierre el 25 → una compra del 27/10 se paga en noviembre.
    /// </summary>
    public static DateOnly PeriodoDeFacturacion(DateOnly fecha, int? diaCierre)
    {
        var periodo = Periodo(fecha);
        if (diaCierre is not int cierre) return periodo;
        int ultimoDia = Math.Min(cierre, DateTime.DaysInMonth(fecha.Year, fecha.Month));
        return fecha.Day > ultimoDia ? periodo.AddMonths(1) : periodo;
    }

    /// <summary>
    /// Mes de la cuota 1 a partir del mes en que se paga la cuota <paramref name="cuotaActual"/>.
    /// Ej.: "5/6" en octubre → la cuota 1 fue en junio. Con día de cierre, "el mes" es el de facturación.
    /// </summary>
    public static DateOnly PrimerPeriodo(DateOnly fecha, int cuotaActual, int? diaCierre = null) =>
        PeriodoDeFacturacion(fecha, diaCierre).AddMonths(-(cuotaActual - 1));

    /// <summary>Número de cuota que corresponde a un mes, o 0 si el mes queda fuera del plan.</summary>
    public static int NumeroEnPeriodo(DateOnly primerPeriodo, int cantidadCuotas, DateOnly mes)
    {
        DateOnly p = Periodo(mes);
        int n = (p.Year - primerPeriodo.Year) * 12 + p.Month - primerPeriodo.Month + 1;
        return n >= 1 && n <= cantidadCuotas ? n : 0;
    }

    public static void Validar(decimal montoCuota, int cantidadCuotas, int cuotaActual)
    {
        if (montoCuota <= 0) throw new ArgumentException("El monto debe ser mayor a cero.");
        if (cantidadCuotas < 1 || cantidadCuotas > MaxCuotas) throw new ArgumentException($"La cantidad de cuotas debe estar entre 1 y {MaxCuotas}.");
        if (cuotaActual < 1 || cuotaActual > cantidadCuotas) throw new ArgumentException("La cuota actual debe estar entre 1 y la cantidad de cuotas.");
    }

    /// <summary>Genera una cuota por mes, desde la cuota 1 hasta la última.</summary>
    public static List<Cuota> Generar(Gasto gasto)
    {
        Validar(gasto.MontoCuota, gasto.CantidadCuotas, 1);
        var cuotas = new List<Cuota>(gasto.CantidadCuotas);
        for (int i = 0; i < gasto.CantidadCuotas; i++)
        {
            cuotas.Add(new Cuota
            {
                Numero = i + 1,
                Periodo = gasto.PrimerPeriodo.AddMonths(i),
                Monto = gasto.MontoCuota,
                UserId = gasto.UserId,
            });
        }
        return cuotas;
    }
}
