using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using ControlGastos.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Services;

/// <summary>Ingresos del mes y el balance contra los gastos (cuánto te queda).</summary>
public class IngresoService(AppDbContext db)
{
    /// <summary>Ingresos con fecha dentro del mes, del más reciente al más viejo.</summary>
    public Task<List<Ingreso>> DelMesAsync(DateOnly mes)
    {
        mes = PlanDeCuotas.Periodo(mes);
        var siguiente = mes.AddMonths(1);
        return db.Ingresos.AsNoTracking()
            .Where(i => i.Fecha >= mes && i.Fecha < siguiente)
            .OrderByDescending(i => i.Fecha).ThenByDescending(i => i.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Ingresos del mes menos lo que se paga ese mes (contado, cuota del mes de cada compra y gastos fijos ya cargados),
    /// por moneda: pesos y dólares no se mezclan.
    /// </summary>
    public static Balance Calcular(IEnumerable<Ingreso> ingresos, ResumenMes gastos)
    {
        var lista = ingresos.ToList();
        BalanceMoneda En(Moneda m) => new(m, lista.Where(i => i.Moneda == m).Sum(i => i.Monto), gastos.Total(m));
        return new Balance(En(Moneda.UYU), En(Moneda.USD));
    }
}
