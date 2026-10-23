using Microsoft.AspNetCore.Identity;

namespace ControlGastos.Web.Domain;

public enum TipoTarjeta { Debito = 1, Credito = 2 }

public enum Moneda { UYU = 1, USD = 2 }

/// <summary>Toda entidad que pertenece a un usuario. El DbContext filtra por <see cref="UserId"/> en cada consulta.</summary>
public interface IPerteneceAUsuario
{
    string UserId { get; set; }
}

/// <summary>Usuario de la aplicación. Identity guarda el hash de la contraseña (PBKDF2), nunca la contraseña.</summary>
public class AppUser : IdentityUser
{
    public string Nombre { get; set; } = "";
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}

/// <summary>Medio de pago con un nombre libre ("Itaú débito"). No se guardan números de tarjeta.</summary>
public class Tarjeta : IPerteneceAUsuario
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Nombre { get; set; } = "";
    public TipoTarjeta Tipo { get; set; }
    /// <summary>Color en formato #RRGGBB para distinguirla en listados y gráficos.</summary>
    public string Color { get; set; } = "#059669";
    /// <summary>Una tarjeta inactiva no se ofrece al cargar gastos, pero conserva su historial.</summary>
    public bool Activa { get; set; } = true;
    /// <summary>
    /// Solo crédito: último día que entra en el estado de cuenta del mes. Una compra posterior
    /// se factura el mes siguiente. Null = el mes de la compra.
    /// </summary>
    public int? DiaCierre { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;

    public List<Gasto> Gastos { get; set; } = new();

    /// <summary>Día de cierre que aplica al calcular el mes de facturación (solo tarjetas de crédito).</summary>
    public int? CierreEfectivo => Tipo == TipoTarjeta.Credito ? DiaCierre : null;
}

public class Categoria : IPerteneceAUsuario
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Nombre { get; set; } = "";
    /// <summary>Clase de Bootstrap Icons, por ejemplo "bi-cart".</summary>
    public string Icono { get; set; } = "bi-tag";
    public string Color { get; set; } = "#64748b";

    public List<Gasto> Gastos { get; set; } = new();
}

/// <summary>
/// Un gasto. Si es en cuotas, <see cref="MontoCuota"/> es lo que se paga cada mes y <see cref="Cuotas"/>
/// tiene una fila por mes desde <see cref="PrimerPeriodo"/>. Un gasto al contado es un plan de 1 cuota.
/// </summary>
public class Gasto : IPerteneceAUsuario
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Descripcion { get; set; } = "";
    /// <summary>Texto tal como figura en el estado de cuenta (ej. "MERPAGO*SEP"), opcional.</summary>
    public string? Comercio { get; set; }
    public DateOnly Fecha { get; set; }

    /// <summary>Null = efectivo o transferencia.</summary>
    public int? TarjetaId { get; set; }
    public Tarjeta? Tarjeta { get; set; }

    public int? CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }

    public Moneda Moneda { get; set; } = Moneda.UYU;
    public decimal MontoCuota { get; set; }
    public int CantidadCuotas { get; set; } = 1;
    /// <summary>Mes (primer día) en que se paga la cuota 1.</summary>
    public DateOnly PrimerPeriodo { get; set; }

    public string? Notas { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;

    /// <summary>Si lo generó un gasto recurrente, cuál. Se puede editar o borrar como cualquier gasto.</summary>
    public int? GastoRecurrenteId { get; set; }
    public GastoRecurrente? GastoRecurrente { get; set; }

    public List<Cuota> Cuotas { get; set; } = new();

    public decimal MontoTotal => MontoCuota * CantidadCuotas;
    public bool EnCuotas => CantidadCuotas > 1;
    public bool EsFijo => GastoRecurrenteId != null;
    public DateOnly UltimoPeriodo => PrimerPeriodo.AddMonths(CantidadCuotas - 1);
    public string MedioDePago => Tarjeta?.Nombre ?? "Efectivo / transferencia";
}

/// <summary>Lo que un gasto aporta a un mes. Se regenera cada vez que el gasto cambia.</summary>
public class Cuota : IPerteneceAUsuario
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int GastoId { get; set; }
    public Gasto Gasto { get; set; } = null!;
    public int Numero { get; set; }
    /// <summary>Primer día del mes al que corresponde.</summary>
    public DateOnly Periodo { get; set; }
    public decimal Monto { get; set; }
}

/// <summary>
/// Gasto fijo que se repite todos los meses (alquiler, facultad, suscripciones). Es una plantilla:
/// genera un <see cref="Gasto"/> por mes desde <see cref="Desde"/> hasta el mes en curso.
/// </summary>
public class GastoRecurrente : IPerteneceAUsuario
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Descripcion { get; set; } = "";

    public int? TarjetaId { get; set; }
    public Tarjeta? Tarjeta { get; set; }
    public int? CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }

    public Moneda Moneda { get; set; } = Moneda.UYU;
    public decimal Monto { get; set; }
    /// <summary>Día del mes en que se cobra (1–31; en meses más cortos, el último día).</summary>
    public int DiaDelMes { get; set; } = 1;
    /// <summary>Primer mes (primer día del mes).</summary>
    public DateOnly Desde { get; set; }
    /// <summary>Último mes, o null si no tiene fin.</summary>
    public DateOnly? Hasta { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>
    /// Último mes ya generado. Garantiza que cada mes se genere una sola vez: si borras el gasto
    /// de un mes, no vuelve a aparecer.
    /// </summary>
    public DateOnly? GeneradoHasta { get; set; }
    public string? Notas { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;

    public List<Gasto> Gastos { get; set; } = new();

    public string MedioDePago => Tarjeta?.Nombre ?? "Efectivo / transferencia";

    /// <summary>Si corresponde cobrarlo en ese mes (sin mirar si está activo).</summary>
    public bool AplicaEn(DateOnly mes)
    {
        var p = PlanDeCuotas.Periodo(mes);
        return p >= Desde && (Hasta == null || p <= Hasta);
    }

    /// <summary>Fecha del cobro en un mes, con el día acotado al largo del mes.</summary>
    public DateOnly FechaEn(DateOnly mes) =>
        new(mes.Year, mes.Month, Math.Min(DiaDelMes, DateTime.DaysInMonth(mes.Year, mes.Month)));
}

/// <summary>Cotización del dólar (en pesos) que el usuario usa para un mes. Sirve para ver todo "pesificado".</summary>
public class TipoCambio : IPerteneceAUsuario
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    /// <summary>Primer día del mes.</summary>
    public DateOnly Mes { get; set; }
    /// <summary>Cuántos pesos vale 1 dólar ese mes.</summary>
    public decimal UsdEnPesos { get; set; }
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;
}

/// <summary>Límite mensual de gasto para una categoría, en una moneda.</summary>
public class Presupuesto : IPerteneceAUsuario
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int CategoriaId { get; set; }
    public Categoria Categoria { get; set; } = null!;
    public Moneda Moneda { get; set; } = Moneda.UYU;
    public decimal MontoMensual { get; set; }
}

/// <summary>
/// Cotización mensual del dólar publicada por el BCU (promedio del mes). Es un dato público, compartido
/// por todos los usuarios: no implementa <see cref="IPerteneceAUsuario"/> y no tiene datos personales.
/// </summary>
public class CotizacionBcu
{
    /// <summary>Primer día del mes (clave).</summary>
    public DateOnly Mes { get; set; }
    public decimal UsdEnPesos { get; set; }
    /// <summary>true cuando el mes ya cerró: el promedio no cambia más y no se vuelve a consultar.</summary>
    public bool Definitiva { get; set; }
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;
}
