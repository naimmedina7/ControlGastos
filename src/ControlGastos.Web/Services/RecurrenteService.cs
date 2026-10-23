using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Services;

public record RecurrenteInput(
    string Descripcion,
    int? TarjetaId,
    int? CategoriaId,
    Moneda Moneda,
    decimal Monto,
    int DiaDelMes,
    DateOnly Desde,
    DateOnly? Hasta,
    string? Notas);

/// <summary>
/// Gastos fijos mensuales. Cada recurrente genera un <see cref="Gasto"/> por mes hasta el mes en curso;
/// los meses futuros solo se muestran como "previstos" (no se guardan hasta que llegan).
/// </summary>
public class RecurrenteService(AppDbContext db, ILogger<RecurrenteService>? logger = null)
{
    public async Task<GastoRecurrente> CrearAsync(RecurrenteInput input, DateOnly mesActual)
    {
        var r = new GastoRecurrente();
        await AplicarAsync(r, input, esNuevo: true);
        db.GastosRecurrentes.Add(r);
        await db.SaveChangesAsync();
        // Si empezó en un mes pasado, se generan de una vez los meses que faltan.
        await GenerarPendientesAsync(mesActual);
        return r;
    }

    /// <summary>Los cambios aplican a los meses que todavía no se generaron; los gastos ya generados no se tocan.</summary>
    public async Task<bool> ActualizarAsync(int id, RecurrenteInput input, DateOnly mesActual)
    {
        var r = await db.GastosRecurrentes.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return false;
        await AplicarAsync(r, input, esNuevo: false);
        await db.SaveChangesAsync();
        await GenerarPendientesAsync(mesActual);
        return true;
    }

    /// <summary>Pausa o reanuda. Al reanudar no se generan los meses en que estuvo pausado.</summary>
    public async Task<GastoRecurrente?> CambiarEstadoAsync(int id, DateOnly mesActual)
    {
        var r = await db.GastosRecurrentes.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return null;
        r.Activo = !r.Activo;
        if (r.Activo)
        {
            var mesAnterior = PlanDeCuotas.Periodo(mesActual).AddMonths(-1);
            if (r.GeneradoHasta == null ? r.Desde <= mesAnterior : r.GeneradoHasta < mesAnterior)
                r.GeneradoHasta = mesAnterior;
        }
        await db.SaveChangesAsync();
        if (r.Activo) await GenerarPendientesAsync(mesActual);
        return r;
    }

    /// <summary>Borra la plantilla. Los gastos que ya generó se conservan como gastos comunes.</summary>
    public async Task<int?> EliminarAsync(int id)
    {
        var r = await db.GastosRecurrentes.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return null;
        // Se desvinculan a mano (no se depende del SET NULL del motor), igual que con las categorías.
        var generados = await db.Gastos.Where(g => g.GastoRecurrenteId == id).ToListAsync();
        foreach (var g in generados) g.GastoRecurrenteId = null;
        db.GastosRecurrentes.Remove(r);
        await db.SaveChangesAsync();
        return generados.Count;
    }

    /// <summary>
    /// Crea los gastos de los meses que faltan, desde el último generado hasta <paramref name="mesActual"/>.
    /// Es idempotente: llamarlo varias veces no duplica nada.
    /// </summary>
    public async Task<int> GenerarPendientesAsync(DateOnly mesActual)
    {
        mesActual = PlanDeCuotas.Periodo(mesActual);
        var pendientes = await db.GastosRecurrentes
            .Include(r => r.Tarjeta)
            .Where(r => r.Activo && r.Desde <= mesActual
                        && (r.GeneradoHasta == null || (r.GeneradoHasta < mesActual && (r.Hasta == null || r.GeneradoHasta < r.Hasta))))
            .ToListAsync();
        if (pendientes.Count == 0) return 0;

        int creados = 0;
        foreach (var r in pendientes)
        {
            // Desde el mes siguiente al último generado, pero nunca antes de "Desde" (puede haberse editado).
            DateOnly inicio = r.Desde;
            if (r.GeneradoHasta is DateOnly g && g.AddMonths(1) > inicio) inicio = g.AddMonths(1);
            DateOnly fin = r.Hasta is DateOnly h && h < mesActual ? h : mesActual;
            for (var mes = inicio; mes <= fin; mes = mes.AddMonths(1))
            {
                db.Gastos.Add(CrearGasto(r, mes));
                creados++;
            }
            r.GeneradoHasta = fin >= inicio ? fin : r.GeneradoHasta ?? fin;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Otra request generó lo mismo al mismo tiempo (índice único recurrente + mes): no es un error.
            logger?.LogInformation(ex, "Generación de recurrentes concurrente; se descarta este intento.");
            db.ChangeTracker.Clear();
            return 0;
        }
        return creados;
    }

    /// <summary>Recurrentes activos que van a generar un gasto en ese mes y todavía no lo hicieron.</summary>
    public async Task<List<GastoRecurrente>> PrevistosAsync(DateOnly mes)
    {
        mes = PlanDeCuotas.Periodo(mes);
        var activos = await db.GastosRecurrentes.AsNoTracking()
            .Include(r => r.Tarjeta).Include(r => r.Categoria)
            .Where(r => r.Activo && r.Desde <= mes && (r.Hasta == null || r.Hasta >= mes)
                        && (r.GeneradoHasta == null || r.GeneradoHasta < mes))
            .ToListAsync();
        return activos.OrderBy(r => r.DiaDelMes).ThenBy(r => r.Descripcion).ToList();
    }

    /// <summary>
    /// El gasto de un mes. Con tarjeta de crédito que tiene día de cierre, un cobro posterior al cierre
    /// cae en el estado de cuenta siguiente (misma regla que un gasto cargado a mano).
    /// </summary>
    internal static Gasto CrearGasto(GastoRecurrente r, DateOnly mes)
    {
        var fecha = r.FechaEn(mes);
        var gasto = new Gasto
        {
            UserId = r.UserId,
            Descripcion = r.Descripcion,
            Fecha = fecha,
            TarjetaId = r.TarjetaId,
            CategoriaId = r.CategoriaId,
            Moneda = r.Moneda,
            MontoCuota = r.Monto,
            CantidadCuotas = 1,
            PrimerPeriodo = PlanDeCuotas.PeriodoDeFacturacion(fecha, r.Tarjeta?.CierreEfectivo),
            Notas = r.Notas,
            GastoRecurrenteId = r.Id,
        };
        gasto.Cuotas = PlanDeCuotas.Generar(gasto);
        return gasto;
    }

    private async Task AplicarAsync(GastoRecurrente r, RecurrenteInput input, bool esNuevo)
    {
        string descripcion = (input.Descripcion ?? "").Trim();
        if (descripcion.Length == 0) throw new ValidacionException("La descripción es obligatoria.");
        if (input.Monto <= 0) throw new ValidacionException("El monto debe ser mayor a cero.");
        if (input.DiaDelMes is < 1 or > 31) throw new ValidacionException("El día del mes debe estar entre 1 y 31.");
        if (!Enum.IsDefined(input.Moneda)) throw new ValidacionException("Moneda inválida.");
        var desde = PlanDeCuotas.Periodo(input.Desde);
        DateOnly? hasta = input.Hasta is DateOnly h ? PlanDeCuotas.Periodo(h) : null;
        if (hasta < desde) throw new ValidacionException("El mes final no puede ser anterior al inicial.");

        if (input.TarjetaId is int tarjetaId)
        {
            var tarjeta = await db.Tarjetas.FirstOrDefaultAsync(t => t.Id == tarjetaId)
                ?? throw new ValidacionException("La tarjeta seleccionada no existe.");
            if (!tarjeta.Activa && (esNuevo || r.TarjetaId != tarjetaId))
                throw new ValidacionException("La tarjeta seleccionada está desactivada.");
        }
        if (input.CategoriaId is int categoriaId && !await db.Categorias.AnyAsync(c => c.Id == categoriaId))
            throw new ValidacionException("La categoría seleccionada no existe.");

        r.Descripcion = descripcion;
        r.TarjetaId = input.TarjetaId;
        r.CategoriaId = input.CategoriaId;
        r.Moneda = input.Moneda;
        r.Monto = decimal.Round(input.Monto, 2);
        r.DiaDelMes = input.DiaDelMes;
        r.Desde = desde;
        r.Hasta = hasta;
        r.Notas = string.IsNullOrWhiteSpace(input.Notas) ? null : input.Notas.Trim();
    }
}
