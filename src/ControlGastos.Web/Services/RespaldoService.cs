using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Services;

// ---------------- Formato del archivo de respaldo (versión 2) ----------------
// Los Id son referencias internas del archivo: al restaurar se crean filas nuevas y se remapean.
// Versión 2 agrega los ingresos. Un respaldo de la versión 1 (sin ingresos) se sigue pudiendo restaurar.

public record RespaldoTarjeta(int Id, string Nombre, TipoTarjeta Tipo, string Color, bool Activa, int? DiaCierre);
public record RespaldoCategoria(int Id, string Nombre, string Icono, string Color);
public record RespaldoRecurrente(int Id, string Descripcion, int? TarjetaId, int? CategoriaId, Moneda Moneda, decimal Monto,
    int DiaDelMes, DateOnly Desde, DateOnly? Hasta, bool Activo, DateOnly? GeneradoHasta, string? Notas);
public record RespaldoGasto(string Descripcion, string? Comercio, DateOnly Fecha, int? TarjetaId, int? CategoriaId, Moneda Moneda,
    decimal MontoCuota, int CantidadCuotas, DateOnly PrimerPeriodo, string? Notas, int? RecurrenteId, DateTime CreadoEn);
public record RespaldoTipoCambio(DateOnly Mes, decimal UsdEnPesos);
public record RespaldoPresupuesto(int CategoriaId, Moneda Moneda, decimal MontoMensual);
public record RespaldoIngreso(string Descripcion, TipoIngreso Tipo, DateOnly Fecha, Moneda Moneda, decimal Monto, string? Notas, DateTime CreadoEn);

public record Respaldo(
    string App,
    int Version,
    DateTime ExportadoEn,
    List<RespaldoTarjeta> Tarjetas,
    List<RespaldoCategoria> Categorias,
    List<RespaldoRecurrente> GastosFijos,
    List<RespaldoGasto> Gastos,
    List<RespaldoTipoCambio> TiposCambio,
    List<RespaldoPresupuesto> Presupuestos,
    List<RespaldoIngreso>? Ingresos = null);

public record ResultadoRestauracion(int Tarjetas, int Categorias, int GastosFijos, int Gastos, int TiposCambio, int Presupuestos, int Ingresos = 0);

/// <summary>
/// Respaldo de los datos de un usuario en un archivo JSON, y restauración desde ese archivo.
/// Restaurar reemplaza todos los datos del usuario actual (y solo los suyos) en una transacción.
/// </summary>
public partial class RespaldoService(AppDbContext db)
{
    public const string NombreApp = "Mis·Gastos";
    public const int VersionActual = 2;
    /// <summary>Versiones de archivo que se pueden restaurar.</summary>
    public const int VersionMinima = 1;
    public const int MaxBytes = 4_000_000;          // Vercel limita el cuerpo de la request a ~4,5 MB
    public const int MaxGastos = 20_000;

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        // Rechaza campos inesperados: un archivo que no es un respaldo de la app no se acepta "a medias".
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")] private static partial Regex ColorValido();
    [GeneratedRegex("^bi-[a-z0-9-]{1,36}$")] private static partial Regex IconoValido();

    public async Task<byte[]> ExportarAsync(DateTime ahoraUtc)
    {
        var tarjetas = await db.Tarjetas.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        var categorias = await db.Categorias.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        var fijos = await db.GastosRecurrentes.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        var gastos = await db.Gastos.AsNoTracking().OrderBy(g => g.Id).ToListAsync();
        var tipos = await db.TiposCambio.AsNoTracking().OrderBy(t => t.Mes).ToListAsync();
        var presupuestos = await db.Presupuestos.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        var ingresos = await db.Ingresos.AsNoTracking().OrderBy(i => i.Id).ToListAsync();

        var respaldo = new Respaldo(
            NombreApp, VersionActual, ahoraUtc,
            tarjetas.Select(t => new RespaldoTarjeta(t.Id, t.Nombre, t.Tipo, t.Color, t.Activa, t.DiaCierre)).ToList(),
            categorias.Select(c => new RespaldoCategoria(c.Id, c.Nombre, c.Icono, c.Color)).ToList(),
            fijos.Select(r => new RespaldoRecurrente(r.Id, r.Descripcion, r.TarjetaId, r.CategoriaId, r.Moneda, r.Monto, r.DiaDelMes,
                r.Desde, r.Hasta, r.Activo, r.GeneradoHasta, r.Notas)).ToList(),
            gastos.Select(g => new RespaldoGasto(g.Descripcion, g.Comercio, g.Fecha, g.TarjetaId, g.CategoriaId, g.Moneda, g.MontoCuota,
                g.CantidadCuotas, g.PrimerPeriodo, g.Notas, g.GastoRecurrenteId, g.CreadoEn)).ToList(),
            tipos.Select(t => new RespaldoTipoCambio(t.Mes, t.UsdEnPesos)).ToList(),
            presupuestos.Select(p => new RespaldoPresupuesto(p.CategoriaId, p.Moneda, p.MontoMensual)).ToList(),
            ingresos.Select(i => new RespaldoIngreso(i.Descripcion, i.Tipo, i.Fecha, i.Moneda, i.Monto, i.Notas, i.CreadoEn)).ToList());

        return JsonSerializer.SerializeToUtf8Bytes(respaldo, Json);
    }

    /// <summary>Lee y valida el archivo. Lanza <see cref="ValidacionException"/> con un mensaje claro si algo no cuadra.</summary>
    public static Respaldo Leer(Stream archivo)
    {
        Respaldo? r;
        try { r = JsonSerializer.Deserialize<Respaldo>(archivo, Json); }
        catch (JsonException) { throw new ValidacionException("El archivo no es un respaldo válido de Mis·Gastos."); }
        if (r == null || r.App != NombreApp) throw new ValidacionException("El archivo no es un respaldo válido de Mis·Gastos.");
        if (r.Version < VersionMinima || r.Version > VersionActual) throw new ValidacionException($"Versión de respaldo no soportada ({r.Version}).");
        if (r.Version >= 2 && r.Ingresos == null) throw new ValidacionException("Respaldo inválido: faltan secciones.");
        r = r with { Ingresos = r.Ingresos ?? new() };
        Validar(r);
        return r;
    }

    private static void Validar(Respaldo r)
    {
        [DoesNotReturn] static void Error(string msg) => throw new ValidacionException("Respaldo inválido: " + msg);
        static bool Texto(string? s, int max, bool requerido = true) => s == null ? !requerido : (!requerido || s.Trim().Length > 0) && s.Length <= max;

        if (r.Tarjetas == null || r.Categorias == null || r.GastosFijos == null || r.Gastos == null || r.TiposCambio == null || r.Presupuestos == null)
            Error("faltan secciones.");
        if (r.Gastos!.Count > MaxGastos || r.Tarjetas!.Count > 200 || r.Categorias!.Count > 500 || r.GastosFijos!.Count > 1000
            || r.TiposCambio!.Count > 1000 || r.Presupuestos!.Count > 1000 || r.Ingresos!.Count > MaxGastos)
            Error("demasiados elementos.");

        var tarjetas = new HashSet<int>();
        foreach (var t in r.Tarjetas)
        {
            if (!tarjetas.Add(t.Id) || !Texto(t.Nombre, 60) || !Enum.IsDefined(t.Tipo) || !ColorValido().IsMatch(t.Color ?? "")
                || t.DiaCierre is < 1 or > 31) Error($"tarjeta \"{t.Nombre}\".");
        }
        if (r.Tarjetas.Select(t => t.Nombre.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Tarjetas.Count) Error("tarjetas con el mismo nombre.");

        var categorias = new HashSet<int>();
        foreach (var c in r.Categorias)
        {
            if (!categorias.Add(c.Id) || !Texto(c.Nombre, 60) || !IconoValido().IsMatch(c.Icono ?? "") || !ColorValido().IsMatch(c.Color ?? ""))
                Error($"categoría \"{c.Nombre}\".");
        }
        if (r.Categorias.Select(c => c.Nombre.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Categorias.Count) Error("categorías con el mismo nombre.");

        bool RefTarjeta(int? id) => id == null || tarjetas.Contains(id.Value);
        bool RefCategoria(int? id) => id == null || categorias.Contains(id.Value);

        var fijos = new HashSet<int>();
        foreach (var f in r.GastosFijos)
        {
            if (!fijos.Add(f.Id) || !Texto(f.Descripcion, 120) || !Texto(f.Notas, 500, false) || !Enum.IsDefined(f.Moneda)
                || f.Monto <= 0 || f.Monto > 99_999_999 || f.DiaDelMes is < 1 or > 31 || f.Hasta < f.Desde
                || !RefTarjeta(f.TarjetaId) || !RefCategoria(f.CategoriaId)) Error($"gasto fijo \"{f.Descripcion}\".");
        }

        var porFijoYMes = new HashSet<(int, DateOnly)>();
        foreach (var g in r.Gastos)
        {
            if (!Texto(g.Descripcion, 120) || !Texto(g.Comercio, 120, false) || !Texto(g.Notas, 500, false) || !Enum.IsDefined(g.Moneda)
                || g.MontoCuota <= 0 || g.MontoCuota > 99_999_999 || g.CantidadCuotas is < 1 or > PlanDeCuotas.MaxCuotas
                || g.PrimerPeriodo.Day != 1 || !RefTarjeta(g.TarjetaId) || !RefCategoria(g.CategoriaId)
                || (g.RecurrenteId is int rid && (!fijos.Contains(rid) || !porFijoYMes.Add((rid, g.PrimerPeriodo)))))
                Error($"gasto \"{g.Descripcion}\" del {g.Fecha:dd/MM/yyyy}.");
        }

        if (r.TiposCambio.Any(t => t.Mes.Day != 1 || t.UsdEnPesos <= 0 || t.UsdEnPesos > 100_000)
            || r.TiposCambio.Select(t => t.Mes).Distinct().Count() != r.TiposCambio.Count) Error("tipos de cambio.");

        if (r.Presupuestos.Any(p => !categorias.Contains(p.CategoriaId) || !Enum.IsDefined(p.Moneda) || p.MontoMensual <= 0 || p.MontoMensual > 99_999_999)
            || r.Presupuestos.Select(p => (p.CategoriaId, p.Moneda)).Distinct().Count() != r.Presupuestos.Count) Error("presupuestos.");

        foreach (var i in r.Ingresos)
        {
            if (!Texto(i.Descripcion, 120) || !Texto(i.Notas, 500, false) || !Enum.IsDefined(i.Tipo) || !Enum.IsDefined(i.Moneda)
                || i.Monto <= 0 || i.Monto > 99_999_999) Error($"ingreso \"{i.Descripcion}\" del {i.Fecha:dd/MM/yyyy}.");
        }
    }

    /// <summary>Reemplaza todos los datos del usuario actual por los del respaldo. Todo o nada.</summary>
    public async Task<ResultadoRestauracion> RestaurarAsync(Respaldo r)
    {
        if (db.UsuarioActualId == null) throw new InvalidOperationException("No hay un usuario para restaurar.");

        await using var tx = await db.Database.BeginTransactionAsync();

        // Borrado de los datos actuales. El filtro global limita cada DELETE al usuario actual.
        await db.Ingresos.ExecuteDeleteAsync();
        await db.Cuotas.ExecuteDeleteAsync();
        await db.Gastos.ExecuteDeleteAsync();
        await db.GastosRecurrentes.ExecuteDeleteAsync();
        await db.Presupuestos.ExecuteDeleteAsync();
        await db.TiposCambio.ExecuteDeleteAsync();
        await db.Tarjetas.ExecuteDeleteAsync();
        await db.Categorias.ExecuteDeleteAsync();
        db.ChangeTracker.Clear();

        var tarjetas = r.Tarjetas.ToDictionary(t => t.Id, t => new Tarjeta
        {
            Nombre = t.Nombre.Trim(), Tipo = t.Tipo, Color = t.Color.ToLowerInvariant(), Activa = t.Activa,
            DiaCierre = t.Tipo == TipoTarjeta.Credito ? t.DiaCierre : null,
        });
        var categorias = r.Categorias.ToDictionary(c => c.Id, c => new Categoria { Nombre = c.Nombre.Trim(), Icono = c.Icono, Color = c.Color.ToLowerInvariant() });
        db.Tarjetas.AddRange(tarjetas.Values);
        db.Categorias.AddRange(categorias.Values);
        await db.SaveChangesAsync();

        int? T(int? id) => id is int i ? tarjetas[i].Id : null;
        int? C(int? id) => id is int i ? categorias[i].Id : null;

        var fijos = r.GastosFijos.ToDictionary(f => f.Id, f => new GastoRecurrente
        {
            Descripcion = f.Descripcion.Trim(), TarjetaId = T(f.TarjetaId), CategoriaId = C(f.CategoriaId), Moneda = f.Moneda,
            Monto = decimal.Round(f.Monto, 2), DiaDelMes = f.DiaDelMes, Desde = PlanDeCuotas.Periodo(f.Desde),
            Hasta = f.Hasta is DateOnly h ? PlanDeCuotas.Periodo(h) : null, Activo = f.Activo,
            GeneradoHasta = f.GeneradoHasta is DateOnly g ? PlanDeCuotas.Periodo(g) : null, Notas = f.Notas,
        });
        db.GastosRecurrentes.AddRange(fijos.Values);
        db.TiposCambio.AddRange(r.TiposCambio.Select(t => new TipoCambio { Mes = t.Mes, UsdEnPesos = decimal.Round(t.UsdEnPesos, 4) }));
        db.Presupuestos.AddRange(r.Presupuestos.Select(p => new Presupuesto { CategoriaId = C(p.CategoriaId)!.Value, Moneda = p.Moneda, MontoMensual = decimal.Round(p.MontoMensual, 2) }));
        db.Ingresos.AddRange(r.Ingresos!.Select(i => new Ingreso
        {
            Descripcion = i.Descripcion.Trim(), Tipo = i.Tipo, Fecha = i.Fecha, Moneda = i.Moneda, Monto = decimal.Round(i.Monto, 2),
            Notas = string.IsNullOrWhiteSpace(i.Notas) ? null : i.Notas.Trim(), CreadoEn = i.CreadoEn,
        }));
        await db.SaveChangesAsync();

        // Los gastos se insertan en tandas para no cargar miles de entidades a la vez en memoria.
        foreach (var tanda in r.Gastos.Chunk(500))
        {
            foreach (var g in tanda)
            {
                var gasto = new Gasto
                {
                    Descripcion = g.Descripcion.Trim(), Comercio = string.IsNullOrWhiteSpace(g.Comercio) ? null : g.Comercio.Trim(),
                    Fecha = g.Fecha, TarjetaId = T(g.TarjetaId), CategoriaId = C(g.CategoriaId), Moneda = g.Moneda,
                    MontoCuota = decimal.Round(g.MontoCuota, 2), CantidadCuotas = g.CantidadCuotas, PrimerPeriodo = g.PrimerPeriodo,
                    Notas = g.Notas, GastoRecurrenteId = g.RecurrenteId is int rid ? fijos[rid].Id : null,
                    CreadoEn = g.CreadoEn, ActualizadoEn = DateTime.UtcNow,
                };
                gasto.Cuotas = PlanDeCuotas.Generar(gasto);
                db.Gastos.Add(gasto);
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        await tx.CommitAsync();
        return new ResultadoRestauracion(r.Tarjetas.Count, r.Categorias.Count, r.GastosFijos.Count, r.Gastos.Count, r.TiposCambio.Count, r.Presupuestos.Count, r.Ingresos!.Count);
    }
}
