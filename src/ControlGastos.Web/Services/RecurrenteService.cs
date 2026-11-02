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
/// Gastos fijos mensuales. Cada recurrente genera un <see cref="Gasto"/> por mes: hasta el mes en curso
/// y, si miras un mes futuro, hasta ese mes (con un tope de <see cref="MesesAdelante"/>). Más allá, se
/// muestran como "previstos". Los meses futuros ya generados se rehacen si el fijo se edita, se pausa o se borra.
/// </summary>
public class RecurrenteService(AppDbContext db, ILogger<RecurrenteService>? logger = null)
{
    /// <summary>Hasta cuántos meses después del actual se generan gastos fijos al mirar un mes futuro.</summary>
    public const int MesesAdelante = 24;

    /// <summary>Hasta qué mes generar: el que se mira, si es futuro (con tope), o el mes en curso.</summary>
    public static DateOnly HastaQueMes(DateOnly mesActual, DateOnly? mesVisto)
    {
        mesActual = PlanDeCuotas.Periodo(mesActual);
        if (mesVisto is not DateOnly v) return mesActual;
        v = PlanDeCuotas.Periodo(v);
        var tope = mesActual.AddMonths(MesesAdelante);
        return v <= mesActual ? mesActual : v > tope ? tope : v;
    }

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

    /// <summary>
    /// Los cambios aplican desde el mes siguiente: los meses pasados y el actual no se tocan, y los meses
    /// futuros que ya se habían generado se rehacen con los valores nuevos (salvo los que ajustaste a mano).
    /// </summary>
    public async Task<bool> ActualizarAsync(int id, RecurrenteInput input, DateOnly mesActual)
    {
        var r = await db.GastosRecurrentes.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return false;
        var anterior = Copia(r);
        await AplicarAsync(r, input, esNuevo: false);
        await QuitarMesesFuturosAsync(r, mesActual, anterior);
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
        if (!r.Activo) await QuitarMesesFuturosAsync(r, mesActual, r);
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

    /// <summary>
    /// Borra la plantilla. Los gastos que ya generó hasta el mes en curso se conservan como gastos comunes;
    /// los de meses futuros (que todavía no llegaron) se borran, salvo los que ajustaste a mano.
    /// </summary>
    public async Task<int?> EliminarAsync(int id, DateOnly mesActual)
    {
        var r = await db.GastosRecurrentes.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return null;
        await QuitarMesesFuturosAsync(r, mesActual, r);
        // Se desvinculan a mano (no se depende del SET NULL del motor), igual que con las categorías.
        var generados = (await db.Gastos.Where(g => g.GastoRecurrenteId == id).ToListAsync())
            .Where(g => db.Entry(g).State != EntityState.Deleted).ToList();
        foreach (var g in generados) g.GastoRecurrenteId = null;
        db.GastosRecurrentes.Remove(r);
        await db.SaveChangesAsync();
        return generados.Count;
    }

    /// <summary>
    /// Crea los gastos de los meses que faltan, desde el último generado hasta <paramref name="hasta"/>
    /// (el mes en curso o un mes futuro que se está mirando). Es idempotente: llamarlo varias veces no duplica nada.
    /// </summary>
    public async Task<int> GenerarPendientesAsync(DateOnly hasta)
    {
        hasta = PlanDeCuotas.Periodo(hasta);
        var pendientes = await db.GastosRecurrentes
            .Include(r => r.Tarjeta)
            .Where(r => r.Activo && r.Desde <= hasta
                        && (r.GeneradoHasta == null || (r.GeneradoHasta < hasta && (r.Hasta == null || r.GeneradoHasta < r.Hasta))))
            .ToListAsync();
        if (pendientes.Count == 0) return 0;

        // Estados de cuenta que ya tienen el gasto de cada fijo. Un fijo da un solo gasto por estado de cuenta:
        // con tarjeta de crédito, dos meses pueden caer en el mismo (si cambió el día de cierre, o un día 31
        // en un mes de 30). Ese gasto se salta en lugar de chocar con el índice único.
        var ids = pendientes.Select(r => (int?)r.Id).ToList();
        var existentes = (await db.Gastos.Where(g => ids.Contains(g.GastoRecurrenteId))
                .Select(g => new { g.GastoRecurrenteId, g.PrimerPeriodo }).ToListAsync())
            .Select(x => (x.GastoRecurrenteId!.Value, x.PrimerPeriodo)).ToHashSet();

        int creados = 0;
        foreach (var r in pendientes)
        {
            if (db.Entry(r).State == EntityState.Detached) db.Attach(r);   // tras un error se limpió el tracker

            // Desde el mes siguiente al último generado, pero nunca antes de "Desde" (puede haberse editado).
            DateOnly inicio = r.Desde;
            if (r.GeneradoHasta is DateOnly g && g.AddMonths(1) > inicio) inicio = g.AddMonths(1);
            DateOnly fin = r.Hasta is DateOnly h && h < hasta ? h : hasta;
            var nuevos = new List<Gasto>();
            for (var mes = inicio; mes <= fin; mes = mes.AddMonths(1))
            {
                var gasto = CrearGasto(r, mes);
                if (existentes.Add((r.Id, gasto.PrimerPeriodo))) nuevos.Add(gasto);
            }
            db.Gastos.AddRange(nuevos);
            r.GeneradoHasta = fin >= inicio ? fin : r.GeneradoHasta ?? fin;

            // Cada fijo se guarda por separado: si uno falla, los demás se cargan igual.
            try
            {
                await db.SaveChangesAsync();
                creados += nuevos.Count;
            }
            catch (DbUpdateException ex)
            {
                // Lo normal es que otra request haya generado lo mismo al mismo tiempo; se reintenta en la próxima visita.
                logger?.LogWarning(ex, "No se pudo generar el gasto fijo {RecurrenteId}; se reintenta en la próxima visita.", r.Id);
                db.ChangeTracker.Clear();
            }
        }
        return creados;
    }

    /// <summary>
    /// Borra los gastos que el fijo ya generó para meses posteriores a <paramref name="mesActual"/> y retrocede
    /// <see cref="GastoRecurrente.GeneradoHasta"/>, para que se vuelvan a generar con los datos vigentes.
    /// Solo se borran los que siguen tal cual los generó <paramref name="plantilla"/>: si ajustaste uno a mano
    /// (otro monto, otra categoría, una nota), se respeta. No guarda: lo hace quien llama.
    /// </summary>
    private async Task QuitarMesesFuturosAsync(GastoRecurrente r, DateOnly mesActual, GastoRecurrente plantilla)
    {
        mesActual = PlanDeCuotas.Periodo(mesActual);
        var siguiente = mesActual.AddMonths(1);
        var futuros = await db.Gastos.Where(g => g.GastoRecurrenteId == r.Id && g.Fecha >= siguiente).ToListAsync();
        db.Gastos.RemoveRange(futuros.Where(g => g.CantidadCuotas == 1 && g.MontoCuota == plantilla.Monto && g.Moneda == plantilla.Moneda
            && g.Descripcion == plantilla.Descripcion && g.TarjetaId == plantilla.TarjetaId && g.CategoriaId == plantilla.CategoriaId
            && g.Notas == plantilla.Notas && g.Comercio == null));
        if (r.GeneradoHasta > mesActual) r.GeneradoHasta = mesActual;
    }

    /// <summary>Los valores con los que el fijo genera gastos, antes de editarlo.</summary>
    private static GastoRecurrente Copia(GastoRecurrente r) => new()
    {
        Id = r.Id, Descripcion = r.Descripcion, TarjetaId = r.TarjetaId, CategoriaId = r.CategoriaId,
        Moneda = r.Moneda, Monto = r.Monto, Notas = r.Notas,
    };

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
