using System.ComponentModel.DataAnnotations;
using ControlGastos.Web.Domain;
using ControlGastos.Web.Services;

namespace ControlGastos.Web.ViewModels;

public class LoginVm
{
    [Required(ErrorMessage = "Ingresa tu correo.")]
    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Ingresa tu contraseña.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public bool Recordarme { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegistroVm
{
    [Required(ErrorMessage = "Ingresa tu nombre.")]
    [StringLength(80, ErrorMessage = "Máximo 80 caracteres.")]
    public string Nombre { get; set; } = "";

    [Required(ErrorMessage = "Ingresa tu correo.")]
    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    [StringLength(254)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Elige una contraseña.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre 8 y 128 caracteres.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Repite la contraseña.")]
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
    [DataType(DataType.Password)]
    public string ConfirmarPassword { get; set; } = "";
}

public class CambiarPasswordVm
{
    [Required(ErrorMessage = "Ingresa tu contraseña actual.")]
    [DataType(DataType.Password)]
    public string Actual { get; set; } = "";

    [Required(ErrorMessage = "Elige una contraseña nueva.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre 8 y 128 caracteres.")]
    [DataType(DataType.Password)]
    public string Nueva { get; set; } = "";

    [Required(ErrorMessage = "Repite la contraseña nueva.")]
    [Compare(nameof(Nueva), ErrorMessage = "Las contraseñas no coinciden.")]
    [DataType(DataType.Password)]
    public string Confirmar { get; set; } = "";
}

public class PerfilVm
{
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreadoEn { get; set; }
    public int CantidadGastos { get; set; }
    public int CantidadTarjetas { get; set; }
    public CambiarPasswordVm Password { get; set; } = new();
}

public class GastoFormVm
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Ingresa una descripción.")]
    [StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
    public string Descripcion { get; set; } = "";

    [StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
    public string? Comercio { get; set; }

    [Required(ErrorMessage = "Ingresa la fecha.")]
    public DateOnly Fecha { get; set; }

    /// <summary>Null = efectivo o transferencia.</summary>
    public int? TarjetaId { get; set; }
    public int? CategoriaId { get; set; }
    public Moneda Moneda { get; set; } = Moneda.UYU;

    [Range(typeof(decimal), "0.01", "99999999", ErrorMessage = "El monto debe ser mayor a cero.")]
    public decimal MontoCuota { get; set; }

    public bool EnCuotas { get; set; }

    [Range(1, PlanDeCuotas.MaxCuotas, ErrorMessage = "Entre 1 y 72 cuotas.")]
    public int CantidadCuotas { get; set; } = 1;

    [Range(1, PlanDeCuotas.MaxCuotas, ErrorMessage = "Cuota inválida.")]
    public int CuotaActual { get; set; } = 1;

    [StringLength(500, ErrorMessage = "Máximo 500 caracteres.")]
    public string? Notas { get; set; }

    public List<Tarjeta> Tarjetas { get; set; } = new();
    public List<Categoria> Categorias { get; set; } = new();
    /// <summary>Descripciones usadas antes, para autocompletar y proponer categoría y tarjeta.</summary>
    public List<SugerenciaGasto> Sugerencias { get; set; } = new();
    /// <summary>Si el gasto lo generó un gasto fijo (solo informativo).</summary>
    public string? RecurrenteDescripcion { get; set; }

    public GastoInput ToInput() => new(
        Descripcion, Comercio, Fecha, TarjetaId, CategoriaId, Moneda, MontoCuota,
        EnCuotas ? CantidadCuotas : 1, EnCuotas ? CuotaActual : 1, Notas);

    public static GastoFormVm Desde(Gasto g) => new()
    {
        Id = g.Id,
        Descripcion = g.Descripcion,
        Comercio = g.Comercio,
        Fecha = g.Fecha,
        TarjetaId = g.TarjetaId,
        CategoriaId = g.CategoriaId,
        Moneda = g.Moneda,
        MontoCuota = g.MontoCuota,
        EnCuotas = g.EnCuotas,
        CantidadCuotas = g.CantidadCuotas,
        // La cuota "de este mes" se calcula sobre el mes de facturación de la fecha (respeta el día de cierre).
        CuotaActual = Math.Max(1, PlanDeCuotas.NumeroEnPeriodo(g.PrimerPeriodo, g.CantidadCuotas,
            PlanDeCuotas.PeriodoDeFacturacion(g.Fecha, g.Tarjeta?.CierreEfectivo))),
        Notas = g.Notas,
        RecurrenteDescripcion = g.GastoRecurrente?.Descripcion,
    };
}

public record SugerenciaGasto(string Descripcion, int? CategoriaId, int? TarjetaId, Moneda Moneda);

public class RecurrenteFormVm : IValidatableObject
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Ingresa una descripción.")]
    [StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
    public string Descripcion { get; set; } = "";

    public int? TarjetaId { get; set; }
    public int? CategoriaId { get; set; }
    public Moneda Moneda { get; set; } = Moneda.UYU;

    [Range(typeof(decimal), "0.01", "99999999", ErrorMessage = "El monto debe ser mayor a cero.")]
    public decimal Monto { get; set; }

    [Range(1, 31, ErrorMessage = "El día debe estar entre 1 y 31.")]
    public int DiaDelMes { get; set; } = 1;

    /// <summary>Mes inicial en formato yyyy-MM (input type=month).</summary>
    [Required(ErrorMessage = "Indica desde qué mes.")]
    public string DesdeMes { get; set; } = "";

    /// <summary>Mes final opcional (yyyy-MM).</summary>
    public string? HastaMes { get; set; }

    [StringLength(500, ErrorMessage = "Máximo 500 caracteres.")]
    public string? Notas { get; set; }

    public List<Tarjeta> Tarjetas { get; set; } = new();
    public List<Categoria> Categorias { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var desde = Infrastructure.Fmt.LeerMes(DesdeMes);
        if (desde == null) yield return new ValidationResult("Mes inicial inválido.", new[] { nameof(DesdeMes) });
        if (!string.IsNullOrWhiteSpace(HastaMes))
        {
            var hasta = Infrastructure.Fmt.LeerMes(HastaMes);
            if (hasta == null) yield return new ValidationResult("Mes final inválido.", new[] { nameof(HastaMes) });
            else if (desde != null && hasta < desde) yield return new ValidationResult("El mes final no puede ser anterior al inicial.", new[] { nameof(HastaMes) });
        }
    }

    public RecurrenteInput ToInput() => new(
        Descripcion, TarjetaId, CategoriaId, Moneda, Monto, DiaDelMes,
        Infrastructure.Fmt.LeerMes(DesdeMes) ?? throw new ValidacionException("Mes inicial inválido."),
        Infrastructure.Fmt.LeerMes(HastaMes),
        Notas);

    public static RecurrenteFormVm Desde(GastoRecurrente r) => new()
    {
        Id = r.Id,
        Descripcion = r.Descripcion,
        TarjetaId = r.TarjetaId,
        CategoriaId = r.CategoriaId,
        Moneda = r.Moneda,
        Monto = r.Monto,
        DiaDelMes = r.DiaDelMes,
        DesdeMes = Infrastructure.Fmt.MesParam(r.Desde),
        HastaMes = r.Hasta is DateOnly h ? Infrastructure.Fmt.MesParam(h) : null,
        Notas = r.Notas,
    };
}

public class RecurrentesVm
{
    public DateOnly MesActual { get; set; }
    public List<GastoRecurrente> Lista { get; set; } = new();
    public IEnumerable<GastoRecurrente> Activos => Lista.Where(r => r.Activo && (r.Hasta == null || r.Hasta >= MesActual));
    public decimal TotalMensual(Moneda m) => Activos.Where(r => r.Moneda == m && r.AplicaEn(MesActual)).Sum(r => r.Monto);
}

public class PlanesVm
{
    public DateOnly Mes { get; set; }
    public List<PlanActivo> Planes { get; set; } = new();
    public decimal Saldo(Moneda m) => Planes.Where(p => p.Gasto.Moneda == m).Sum(p => p.Saldo);
    public decimal CuotaDelMes(Moneda m) => Planes.Where(p => p.Gasto.Moneda == m && p.CuotaEsteMes > 0).Sum(p => p.Gasto.MontoCuota);
}

public class TarjetaFormVm
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Ponle un nombre, por ejemplo \"Itaú débito\".")]
    [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
    public string Nombre { get; set; } = "";

    public TipoTarjeta Tipo { get; set; } = TipoTarjeta.Credito;

    /// <summary>Solo crédito. Vacío = cada compra cuenta en su mes.</summary>
    [Range(1, 31, ErrorMessage = "El día de cierre debe estar entre 1 y 31.")]
    public int? DiaCierre { get; set; }

    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Color inválido.")]
    public string Color { get; set; } = "#059669";
}

public class CategoriaFormVm
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Ponle un nombre.")]
    [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
    public string Nombre { get; set; } = "";

    [Required]
    [RegularExpression("^bi-[a-z0-9-]{1,36}$", ErrorMessage = "Ícono inválido.")]
    public string Icono { get; set; } = "bi-tag";

    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Color inválido.")]
    public string Color { get; set; } = "#64748b";

    public static readonly string[] Iconos =
    {
        "bi-cart", "bi-cup-hot", "bi-fuel-pump", "bi-bus-front", "bi-house", "bi-heart-pulse", "bi-mortarboard",
        "bi-play-btn", "bi-lightning-charge", "bi-wifi", "bi-laptop", "bi-phone", "bi-bag", "bi-ticket-perforated",
        "bi-bank", "bi-briefcase", "bi-gift", "bi-airplane", "bi-scissors", "bi-controller", "bi-basket", "bi-tools",
        "bi-capsule", "bi-tag", "bi-three-dots",
    };
}

public class TarjetaFilaVm
{
    public Tarjeta Tarjeta { get; set; } = null!;
    public int CantidadGastos { get; set; }
    public decimal MesUYU { get; set; }
    public decimal MesUSD { get; set; }
}

public class CategoriaFilaVm
{
    public Categoria Categoria { get; set; } = null!;
    public int CantidadGastos { get; set; }
}

public class PanelVm
{
    public string Nombre { get; set; } = "";
    public DateOnly Mes { get; set; }
    public bool EsMesActual { get; set; }
    public ResumenMes Resumen { get; set; } = null!;
    public ResumenMes? MesAnterior { get; set; }
    public List<MesProyectado> Proximos { get; set; } = new();
    public List<EstadoPresupuesto> Presupuestos { get; set; } = new();
}

public class GastosMesVm
{
    public DateOnly Mes { get; set; }
    public bool EsMesActual { get; set; }
    public ResumenMes Resumen { get; set; } = null!;
    /// <summary>Gastos fijos que todavía no se cargaron en este mes (meses futuros).</summary>
    public List<GastoRecurrente> Previstos { get; set; } = new();
}

// ---------------- Etapa 3: reportes ----------------

public class ReporteVm
{
    public Reporte Reporte { get; set; } = null!;
    /// <summary>"6", "12", "anio", "anio-anterior" o "personalizado".</summary>
    public string Rango { get; set; } = "12";
    public int MesesFuturos { get; set; }
}

// ---------------- Etapa 4: importación ----------------

public class ImportarVm
{
    [Required(ErrorMessage = "Pega el texto del estado de cuenta.")]
    [StringLength(EstadoDeCuenta.MaxCaracteres, ErrorMessage = "El texto es demasiado largo.")]
    public string Texto { get; set; } = "";

    public int? TarjetaId { get; set; }

    /// <summary>Mes del estado de cuenta (yyyy-MM). Vacío = se deduce de las fechas.</summary>
    public string? MesEstado { get; set; }

    public List<Tarjeta> Tarjetas { get; set; } = new();
    public List<Categoria> Categorias { get; set; } = new();
    public AnalisisImportacion? Analisis { get; set; }
}

/// <summary>Lo que el usuario decide de cada línea. El resto de los datos se vuelve a leer del texto.</summary>
public class ItemConfirmacion
{
    public int Numero { get; set; }
    public bool Incluir { get; set; }
    [StringLength(120)]
    public string? Descripcion { get; set; }
    public int? CategoriaId { get; set; }
}

public class ConfirmarImportacionVm
{
    [Required]
    [StringLength(EstadoDeCuenta.MaxCaracteres)]
    public string Texto { get; set; } = "";
    public int? TarjetaId { get; set; }
    [Required]
    public string MesEstado { get; set; } = "";
    public List<ItemConfirmacion> Items { get; set; } = new();
}

// ---------------- Etapa 4: tipo de cambio y presupuestos ----------------

public class TipoCambioFormVm
{
    [Required(ErrorMessage = "Elige el mes.")]
    public string Mes { get; set; } = "";

    [Range(typeof(decimal), "0.0001", "100000", ErrorMessage = "Ingresa una cotización mayor a cero.")]
    public decimal UsdEnPesos { get; set; }
}

/// <summary>Un mes en la pantalla de tipo de cambio: lo del BCU, el ajuste manual y el valor que se usa.</summary>
public record FilaCotizacion(DateOnly Mes, CotizacionBcu? Bcu, TipoCambio? Manual, decimal? Usado, bool EsMesActual);

public class TiposCambioVm
{
    public List<FilaCotizacion> Meses { get; set; } = new();
    public List<TipoCambio> AjustesAnteriores { get; set; } = new();
    /// <summary>Meses con gastos en dólares que no tienen ninguna cotización (ni propia, ni del BCU, ni anterior).</summary>
    public List<DateOnly> MesesUsdSinCotizacion { get; set; } = new();
    public DateTime? UltimaActualizacionBcu { get; set; }
    public TipoCambioFormVm Form { get; set; } = new();
}

public class PresupuestoFormVm
{
    [Required(ErrorMessage = "Elige una categoría.")]
    public int? CategoriaId { get; set; }
    public Moneda Moneda { get; set; } = Moneda.UYU;

    [Range(typeof(decimal), "0.01", "99999999", ErrorMessage = "El presupuesto debe ser mayor a cero.")]
    public decimal MontoMensual { get; set; }
}

public class PresupuestosVm
{
    public DateOnly Mes { get; set; }
    public List<EstadoPresupuesto> Estados { get; set; } = new();
    public List<Categoria> Categorias { get; set; } = new();
    public PresupuestoFormVm Form { get; set; } = new();
}
