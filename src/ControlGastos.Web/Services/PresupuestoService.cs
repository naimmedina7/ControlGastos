using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Services;

public enum NivelPresupuesto { Ok, Cerca, Excedido }

public record EstadoPresupuesto(Presupuesto Presupuesto, decimal Gastado)
{
    /// <summary>Desde qué porcentaje se avisa que está cerca del límite.</summary>
    public const decimal UmbralAviso = 80;

    public decimal Porcentaje => Presupuesto.MontoMensual <= 0 ? 0 : decimal.Round(Gastado / Presupuesto.MontoMensual * 100, 1);
    public decimal Disponible => Presupuesto.MontoMensual - Gastado;
    public NivelPresupuesto Nivel => Porcentaje > 100 ? NivelPresupuesto.Excedido : Porcentaje >= UmbralAviso ? NivelPresupuesto.Cerca : NivelPresupuesto.Ok;
}

/// <summary>Presupuestos mensuales por categoría y cuánto se lleva gastado de cada uno.</summary>
public class PresupuestoService(AppDbContext db)
{
    public async Task<List<EstadoPresupuesto>> EstadoAsync(DateOnly mes)
    {
        mes = PlanDeCuotas.Periodo(mes);
        var presupuestos = await db.Presupuestos.AsNoTracking().Include(p => p.Categoria).ToListAsync();
        if (presupuestos.Count == 0) return new();

        var gastado = await db.Cuotas.AsNoTracking()
            .Where(c => c.Periodo == mes && c.Gasto.CategoriaId != null)
            .Select(c => new { CategoriaId = c.Gasto.CategoriaId!.Value, c.Gasto.Moneda, c.Monto })
            .ToListAsync();

        return presupuestos
            .Select(p => new EstadoPresupuesto(p, gastado.Where(g => g.CategoriaId == p.CategoriaId && g.Moneda == p.Moneda).Sum(g => g.Monto)))
            .OrderByDescending(e => e.Porcentaje)
            .ToList();
    }

    /// <summary>Crea o actualiza el presupuesto de una categoría en una moneda.</summary>
    public async Task GuardarAsync(int categoriaId, Moneda moneda, decimal monto)
    {
        if (monto <= 0) throw new ValidacionException("El presupuesto debe ser mayor a cero.");
        if (!Enum.IsDefined(moneda)) throw new ValidacionException("Moneda inválida.");
        if (!await db.Categorias.AnyAsync(c => c.Id == categoriaId))
            throw new ValidacionException("La categoría seleccionada no existe.");

        var existente = await db.Presupuestos.FirstOrDefaultAsync(p => p.CategoriaId == categoriaId && p.Moneda == moneda);
        if (existente == null)
            db.Presupuestos.Add(new Presupuesto { CategoriaId = categoriaId, Moneda = moneda, MontoMensual = decimal.Round(monto, 2) });
        else
            existente.MontoMensual = decimal.Round(monto, 2);
        await db.SaveChangesAsync();
    }

    public async Task<bool> EliminarAsync(int id)
    {
        var p = await db.Presupuestos.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return false;
        db.Presupuestos.Remove(p);
        await db.SaveChangesAsync();
        return true;
    }
}
