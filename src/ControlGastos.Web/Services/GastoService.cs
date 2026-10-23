using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Services;

/// <summary>Error de validación con un mensaje apto para mostrar al usuario.</summary>
public class ValidacionException(string mensaje) : Exception(mensaje);

public record GastoInput(
    string Descripcion,
    string? Comercio,
    DateOnly Fecha,
    int? TarjetaId,
    int? CategoriaId,
    Moneda Moneda,
    decimal MontoCuota,
    int CantidadCuotas,
    int CuotaActual,
    string? Notas,
    // Mes del estado de cuenta, cuando se conoce (importación): manda sobre la fecha y el día de cierre.
    DateOnly? MesFacturacion = null);

/// <summary>Una cuota que cae en el mes consultado, con su gasto.</summary>
public record LineaDelMes(Gasto Gasto, int Numero, decimal Monto)
{
    public bool EsUltima => Numero == Gasto.CantidadCuotas;
}

public record TotalPorGrupo(string Nombre, string Color, string? Icono, Moneda Moneda, decimal Total, int Cantidad);

public record ResumenMes(
    DateOnly Mes,
    List<LineaDelMes> Lineas,
    Dictionary<Moneda, decimal> Totales,
    Dictionary<Moneda, decimal> PendienteFuturo,
    List<TotalPorGrupo> PorTarjeta,
    List<TotalPorGrupo> PorCategoria)
{
    public decimal Total(Moneda m) => Totales.GetValueOrDefault(m);
    public decimal Pendiente(Moneda m) => PendienteFuturo.GetValueOrDefault(m);
    public int TerminanEsteMes => Lineas.Count(l => l.Gasto.EnCuotas && l.EsUltima);
    public int EnCuotas => Lineas.Count(l => l.Gasto.EnCuotas);
}

/// <summary>Total de un mes. Incluye las cuotas cargadas y los gastos fijos previstos (aún no generados).</summary>
public record MesProyectado(DateOnly Mes, decimal UYU, decimal USD, decimal PrevistoUYU = 0, decimal PrevistoUSD = 0);

/// <summary>Un plan de cuotas que todavía tiene cuotas por pagar.</summary>
public record PlanActivo(Gasto Gasto, int CuotaEsteMes, int Faltan)
{
    /// <summary>Lo que falta pagar, incluida la cuota de este mes.</summary>
    public decimal Saldo => Gasto.MontoCuota * Faltan;
    public int Pagadas => Gasto.CantidadCuotas - Faltan;
}

/// <summary>
/// Reglas de los gastos. Todas las consultas pasan por el filtro por usuario del <see cref="AppDbContext"/>,
/// así que este servicio nunca ve datos de otra cuenta.
/// </summary>
public class GastoService(AppDbContext db)
{
    public async Task<Gasto> CrearAsync(GastoInput input)
    {
        var gasto = new Gasto();
        await AplicarAsync(gasto, input, esNuevo: true);
        db.Gastos.Add(gasto);
        await db.SaveChangesAsync();
        return gasto;
    }

    public async Task<bool> ActualizarAsync(int id, GastoInput input)
    {
        var gasto = await db.Gastos.Include(g => g.Cuotas).FirstOrDefaultAsync(g => g.Id == id);
        if (gasto == null) return false;
        await AplicarAsync(gasto, input, esNuevo: false);
        gasto.ActualizadoEn = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> EliminarAsync(int id)
    {
        var gasto = await db.Gastos.Include(g => g.Cuotas).FirstOrDefaultAsync(g => g.Id == id);
        if (gasto == null) return false;
        db.Gastos.Remove(gasto);
        await db.SaveChangesAsync();
        return true;
    }

    private async Task AplicarAsync(Gasto gasto, GastoInput input, bool esNuevo)
    {
        string descripcion = (input.Descripcion ?? "").Trim();
        if (descripcion.Length == 0) throw new ValidacionException("La descripción es obligatoria.");
        try { PlanDeCuotas.Validar(input.MontoCuota, input.CantidadCuotas, input.CuotaActual); }
        catch (ArgumentException ex) { throw new ValidacionException(ex.Message); }
        if (!Enum.IsDefined(input.Moneda)) throw new ValidacionException("Moneda inválida.");

        // Las FK se validan contra datos del propio usuario (el filtro global oculta los ajenos).
        Tarjeta? tarjeta = null;
        if (input.TarjetaId is int tarjetaId)
        {
            tarjeta = await db.Tarjetas.FirstOrDefaultAsync(t => t.Id == tarjetaId)
                ?? throw new ValidacionException("La tarjeta seleccionada no existe.");
            if (!tarjeta.Activa && (esNuevo || gasto.TarjetaId != tarjetaId))
                throw new ValidacionException("La tarjeta seleccionada está desactivada.");
        }
        if (input.CategoriaId is int categoriaId && !await db.Categorias.AnyAsync(c => c.Id == categoriaId))
            throw new ValidacionException("La categoría seleccionada no existe.");

        gasto.Descripcion = descripcion;
        gasto.Comercio = string.IsNullOrWhiteSpace(input.Comercio) ? null : input.Comercio.Trim();
        gasto.Fecha = input.Fecha;
        gasto.TarjetaId = input.TarjetaId;
        gasto.CategoriaId = input.CategoriaId;
        gasto.Moneda = input.Moneda;
        gasto.MontoCuota = decimal.Round(input.MontoCuota, 2);
        gasto.CantidadCuotas = input.CantidadCuotas;
        // Con tarjeta de crédito con día de cierre, el mes es el del estado de cuenta.
        gasto.PrimerPeriodo = input.MesFacturacion is DateOnly mesEstado
            ? PlanDeCuotas.Periodo(mesEstado).AddMonths(-(input.CuotaActual - 1))
            : PlanDeCuotas.PrimerPeriodo(input.Fecha, input.CuotaActual, tarjeta?.CierreEfectivo);

        // Un gasto fijo generado no puede moverse a un mes donde su recurrente ya generó otro.
        if (gasto.GastoRecurrenteId is int recId && await db.Gastos.AnyAsync(g =>
                g.GastoRecurrenteId == recId && g.PrimerPeriodo == gasto.PrimerPeriodo && g.Id != gasto.Id))
            throw new ValidacionException("Ese gasto fijo ya tiene un gasto en ese mes. Edita aquel o elige otra fecha.");
        gasto.Notas = string.IsNullOrWhiteSpace(input.Notas) ? null : input.Notas.Trim();

        // Las cuotas se regeneran completas: es más simple y evita estados intermedios inconsistentes.
        if (gasto.Cuotas.Count > 0) db.Cuotas.RemoveRange(gasto.Cuotas);
        gasto.Cuotas = PlanDeCuotas.Generar(gasto);
    }

    public async Task<ResumenMes> ResumenAsync(DateOnly mes)
    {
        mes = PlanDeCuotas.Periodo(mes);
        var cuotas = await db.Cuotas
            .Where(c => c.Periodo == mes)
            .Include(c => c.Gasto).ThenInclude(g => g.Tarjeta)
            .Include(c => c.Gasto).ThenInclude(g => g.Categoria)
            .AsNoTracking()
            .ToListAsync();

        var lineas = cuotas
            .Select(c => new LineaDelMes(c.Gasto, c.Numero, c.Monto))
            .OrderByDescending(l => l.Gasto.Fecha).ThenByDescending(l => l.Gasto.Id)
            .ToList();

        // Los montos se agregan en memoria: SQLite no suma decimales en SQL, y el volumen de un mes es chico.
        var futuras = await db.Cuotas
            .Where(c => c.Periodo > mes)
            .Select(c => new { c.Monto, c.Gasto.Moneda })
            .ToListAsync();

        return new ResumenMes(
            mes,
            lineas,
            lineas.GroupBy(l => l.Gasto.Moneda).ToDictionary(g => g.Key, g => g.Sum(l => l.Monto)),
            futuras.GroupBy(f => f.Moneda).ToDictionary(g => g.Key, g => g.Sum(f => f.Monto)),
            Agrupar(lineas, l => l.Gasto.Tarjeta?.Nombre ?? "Efectivo / transferencia", l => l.Gasto.Tarjeta?.Color ?? "#64748b", l => null),
            Agrupar(lineas, l => l.Gasto.Categoria?.Nombre ?? "Sin categoría", l => l.Gasto.Categoria?.Color ?? "#94a3b8", l => l.Gasto.Categoria?.Icono ?? "bi-question-circle"));
    }

    private static List<TotalPorGrupo> Agrupar(List<LineaDelMes> lineas, Func<LineaDelMes, string> nombre, Func<LineaDelMes, string> color, Func<LineaDelMes, string?> icono) =>
        lineas
            .GroupBy(l => (Nombre: nombre(l), l.Gasto.Moneda))
            .Select(g => new TotalPorGrupo(g.Key.Nombre, color(g.First()), icono(g.First()), g.Key.Moneda, g.Sum(l => l.Monto), g.Count()))
            .OrderBy(t => t.Moneda).ThenByDescending(t => t.Total)
            .ToList();

    /// <summary>Total por mes en cada moneda, desde <paramref name="desde"/> durante <paramref name="meses"/> meses.</summary>
    public async Task<List<MesProyectado>> ProyeccionAsync(DateOnly desde, int meses)
    {
        desde = PlanDeCuotas.Periodo(desde);
        DateOnly hasta = desde.AddMonths(meses);
        var filas = await db.Cuotas
            .Where(c => c.Periodo >= desde && c.Periodo < hasta)
            .Select(c => new { c.Periodo, c.Monto, c.Gasto.Moneda })
            .ToListAsync();

        // Gastos fijos de los meses que todavía no se generaron (previstos).
        var recurrentes = await db.GastosRecurrentes.AsNoTracking().Include(r => r.Tarjeta)
            .Where(r => r.Activo && r.Desde < hasta && (r.Hasta == null || r.Hasta >= desde.AddMonths(-1)))
            .ToListAsync();
        var previstos = new List<(DateOnly Periodo, Moneda Moneda, decimal Monto)>();
        foreach (var r in recurrentes)
        {
            // Se mira un mes antes por si el cobro de fin de mes cae en el estado de cuenta siguiente.
            for (var m = desde.AddMonths(-1); m < hasta; m = m.AddMonths(1))
            {
                if (!r.AplicaEn(m) || (r.GeneradoHasta is DateOnly g && m <= g)) continue;
                var periodo = PlanDeCuotas.PeriodoDeFacturacion(r.FechaEn(m), r.Tarjeta?.CierreEfectivo);
                if (periodo >= desde && periodo < hasta) previstos.Add((periodo, r.Moneda, r.Monto));
            }
        }

        return Enumerable.Range(0, meses)
            .Select(i => desde.AddMonths(i))
            .Select(m =>
            {
                decimal pUyu = previstos.Where(p => p.Periodo == m && p.Moneda == Moneda.UYU).Sum(p => p.Monto);
                decimal pUsd = previstos.Where(p => p.Periodo == m && p.Moneda == Moneda.USD).Sum(p => p.Monto);
                return new MesProyectado(
                    m,
                    filas.Where(f => f.Periodo == m && f.Moneda == Moneda.UYU).Sum(f => f.Monto) + pUyu,
                    filas.Where(f => f.Periodo == m && f.Moneda == Moneda.USD).Sum(f => f.Monto) + pUsd,
                    pUyu, pUsd);
            })
            .ToList();
    }

    /// <summary>Compras en cuotas que tienen cuotas en <paramref name="mes"/> o después, ordenadas por fecha de fin.</summary>
    public async Task<List<PlanActivo>> PlanesActivosAsync(DateOnly mes)
    {
        mes = PlanDeCuotas.Periodo(mes);
        var ids = await db.Cuotas.Where(c => c.Periodo >= mes && c.Gasto.CantidadCuotas > 1)
            .Select(c => c.GastoId).Distinct().ToListAsync();
        var gastos = await db.Gastos.AsNoTracking()
            .Include(g => g.Tarjeta).Include(g => g.Categoria)
            .Where(g => ids.Contains(g.Id))
            .ToListAsync();

        return gastos
            .Select(g =>
            {
                int n = PlanDeCuotas.NumeroEnPeriodo(g.PrimerPeriodo, g.CantidadCuotas, mes);
                int faltan = mes < g.PrimerPeriodo ? g.CantidadCuotas : g.CantidadCuotas - n + 1;
                return new PlanActivo(g, n, faltan);
            })
            .OrderBy(p => p.Gasto.UltimoPeriodo).ThenBy(p => p.Gasto.Descripcion)
            .ToList();
    }
}
