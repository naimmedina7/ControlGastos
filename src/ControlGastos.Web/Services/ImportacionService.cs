using System.Text.RegularExpressions;
using ControlGastos.Web.Data;
using ControlGastos.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Services;

/// <summary>Una línea del estado de cuenta lista para revisar antes de importar.</summary>
public class ItemImportacion
{
    public LineaEstadoCuenta Linea { get; init; } = null!;
    public string Descripcion { get; set; } = "";
    public int? CategoriaId { get; set; }
    /// <summary>De dónde salió la categoría: "historial", "regla" o null.</summary>
    public string? OrigenCategoria { get; set; }
    public bool Duplicado { get; set; }
    public bool Incluir { get; set; }
}

public record AnalisisImportacion(DateOnly MesEstado, List<ItemImportacion> Items)
{
    public IEnumerable<ItemImportacion> Gastos => Items.Where(i => i.Linea.Tipo == TipoLinea.Gasto);
    public int Nuevos => Gastos.Count(i => !i.Duplicado);
    public int Duplicados => Gastos.Count(i => i.Duplicado);
    public int Pagos => Items.Count(i => i.Linea.Tipo == TipoLinea.PagoTarjeta);
    public int NoReconocidas => Items.Count(i => i.Linea.Tipo == TipoLinea.NoReconocida);
}

/// <summary>Lo que el usuario confirma de cada línea en la vista previa.</summary>
public record LineaAImportar(
    string Comercio, DateOnly Fecha, Moneda Moneda, decimal Monto, int CuotaActual, int CantidadCuotas,
    string Descripcion, int? CategoriaId);

/// <summary>
/// Importa el texto de un estado de cuenta: interpreta las líneas, propone descripción y categoría
/// (aprendidas del historial del usuario), marca duplicados y crea los gastos en una sola transacción.
/// </summary>
public class ImportacionService(AppDbContext db, GastoService gastos)
{
    public async Task<AnalisisImportacion> AnalizarAsync(string texto, int? tarjetaId, DateOnly? mesEstado)
    {
        var lineas = EstadoDeCuenta.Interpretar(texto);
        var tarjeta = tarjetaId is int id ? await db.Tarjetas.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id) : null;
        var mes = mesEstado is DateOnly m ? PlanDeCuotas.Periodo(m) : MesProbable(lineas, tarjeta?.CierreEfectivo);

        var historial = await HistorialAsync();
        var categorias = await db.Categorias.AsNoTracking().ToListAsync();
        var existentes = await ClavesExistentesAsync();

        var items = new List<ItemImportacion>();
        foreach (var l in lineas)
        {
            var item = new ItemImportacion { Linea = l };
            if (l.Tipo == TipoLinea.Gasto)
            {
                string clave = EstadoDeCuenta.Normalizar(l.Comercio);
                if (historial.TryGetValue(clave, out var previo))
                {
                    item.Descripcion = previo.Descripcion;
                    item.CategoriaId = previo.CategoriaId;
                    item.OrigenCategoria = previo.CategoriaId != null ? "historial" : null;
                }
                else
                {
                    item.Descripcion = EstadoDeCuenta.DescripcionSugerida(l.Comercio);
                }
                if (item.CategoriaId == null && CategoriaPorRegla(clave, categorias) is int porRegla)
                {
                    item.CategoriaId = porRegla;
                    item.OrigenCategoria = "regla";
                }
                item.Duplicado = existentes.Contains(Clave(clave, l.Moneda, l.Monto, l.CantidadCuotas, PrimerPeriodo(mes, l.CuotaActual), l.EnCuotas ? null : l.Fecha));
                item.Incluir = !item.Duplicado;
            }
            items.Add(item);
        }
        return new AnalisisImportacion(mes, items);
    }

    /// <summary>Crea los gastos confirmados. Todo o nada: si una línea falla, no se guarda ninguna.</summary>
    public async Task<int> ImportarAsync(int? tarjetaId, DateOnly mesEstado, IReadOnlyList<LineaAImportar> lineas)
    {
        if (lineas.Count == 0) return 0;
        if (lineas.Count > EstadoDeCuenta.MaxLineas) throw new ValidacionException("Demasiadas líneas para una sola importación.");
        mesEstado = PlanDeCuotas.Periodo(mesEstado);

        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var l in lineas)
        {
            string descripcion = string.IsNullOrWhiteSpace(l.Descripcion) ? EstadoDeCuenta.DescripcionSugerida(l.Comercio) : l.Descripcion.Trim();
            if (descripcion.Length > 120) descripcion = descripcion[..120];
            string comercio = l.Comercio.Trim();
            if (comercio.Length > 120) comercio = comercio[..120];
            await gastos.CrearAsync(new GastoInput(
                descripcion, comercio, l.Fecha, tarjetaId, l.CategoriaId, l.Moneda, l.Monto,
                l.CantidadCuotas, l.CuotaActual, Notas: null, MesFacturacion: mesEstado));
        }
        await tx.CommitAsync();
        return lineas.Count;
    }

    /// <summary>
    /// Mes del estado de cuenta más probable: el mes de facturación de la compra más reciente.
    /// Las cuotas de planes viejos suelen venir fechadas en el mes del estado de cuenta.
    /// </summary>
    public static DateOnly MesProbable(IEnumerable<LineaEstadoCuenta> lineas, int? diaCierre)
    {
        var fechas = lineas.Where(l => l.Tipo == TipoLinea.Gasto).Select(l => l.Fecha).ToList();
        if (fechas.Count == 0) return PlanDeCuotas.Periodo(DateOnly.FromDateTime(DateTime.Today));
        return PlanDeCuotas.PeriodoDeFacturacion(fechas.Max(), diaCierre);
    }

    private static DateOnly PrimerPeriodo(DateOnly mesEstado, int cuotaActual) => mesEstado.AddMonths(-(cuotaActual - 1));

    /// <summary>
    /// Identifica un gasto ya cargado. Un plan en cuotas se reconoce por comercio, monto, cantidad de cuotas
    /// y mes de inicio (así el estado de cuenta del mes siguiente no lo duplica); una compra al contado, además, por la fecha.
    /// </summary>
    private static string Clave(string comercio, Moneda moneda, decimal monto, int cuotas, DateOnly primerPeriodo, DateOnly? fecha) =>
        $"{comercio}|{moneda}|{decimal.Round(monto, 2):0.00}|{cuotas}|{primerPeriodo:yyyy-MM}|{fecha:yyyy-MM-dd}";

    private async Task<HashSet<string>> ClavesExistentesAsync()
    {
        var filas = await db.Gastos.AsNoTracking()
            .Where(g => g.Comercio != null)
            .Select(g => new { g.Comercio, g.Moneda, g.MontoCuota, g.CantidadCuotas, g.PrimerPeriodo, g.Fecha })
            .ToListAsync();
        return filas
            .Select(g => Clave(EstadoDeCuenta.Normalizar(g.Comercio), g.Moneda, g.MontoCuota, g.CantidadCuotas, g.PrimerPeriodo, g.CantidadCuotas > 1 ? null : g.Fecha))
            .ToHashSet();
    }

    /// <summary>Comercio normalizado → descripción y categoría del gasto más reciente con ese comercio.</summary>
    private async Task<Dictionary<string, (string Descripcion, int? CategoriaId)>> HistorialAsync()
    {
        var filas = await db.Gastos.AsNoTracking()
            .Where(g => g.Comercio != null)
            .OrderByDescending(g => g.ActualizadoEn)
            .Select(g => new { g.Comercio, g.Descripcion, g.CategoriaId })
            .Take(2000)
            .ToListAsync();
        var dic = new Dictionary<string, (string, int?)>();
        foreach (var f in filas)
        {
            string clave = EstadoDeCuenta.Normalizar(f.Comercio);
            if (!dic.ContainsKey(clave)) dic[clave] = (f.Descripcion, f.CategoriaId);
        }
        return dic;
    }

    /// <summary>Aplica las reglas de partida, buscando palabras completas (UTE no coincide con COMPUTE).</summary>
    private static int? CategoriaPorRegla(string comercioNormalizado, List<Categoria> categorias)
    {
        foreach (var (contiene, nombre) in EstadoDeCuenta.ReglasBase)
        {
            if (!Regex.IsMatch(comercioNormalizado, $@"(?<![A-Z0-9]){Regex.Escape(contiene)}(?![A-Z0-9])")) continue;
            var cat = categorias.FirstOrDefault(c => EstadoDeCuenta.Normalizar(c.Nombre) == EstadoDeCuenta.Normalizar(nombre));
            if (cat != null) return cat.Id;
        }
        return null;
    }
}
