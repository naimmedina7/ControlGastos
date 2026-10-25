using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ControlGastos.Web.Domain;

public enum TipoLinea
{
    /// <summary>Una compra (contado o cuota de un plan).</summary>
    Gasto,
    /// <summary>"RECIBO DE PAGO" o un monto negativo: es un pago a la tarjeta, no un gasto.</summary>
    PagoTarjeta,
    /// <summary>No tiene el formato esperado (encabezados, totales, líneas cortadas).</summary>
    NoReconocida,
}

/// <summary>Una línea del estado de cuenta, ya interpretada.</summary>
public record LineaEstadoCuenta(
    int Numero,
    string Original,
    TipoLinea Tipo,
    string Comercio = "",
    DateOnly Fecha = default,
    Moneda Moneda = Moneda.UYU,
    decimal Monto = 0,
    int CuotaActual = 1,
    int CantidadCuotas = 1,
    string? Motivo = null)
{
    public bool EnCuotas => CantidadCuotas > 1;
}

/// <summary>
/// Interpreta el texto copiado de un estado de cuenta de tarjeta. Reconoce dos formatos (columnas separadas
/// por tabulador o espacios), según el banco:
/// <code>**** 1012  MERPAGO*MERCADOLIBRE  Plan Pagos  05/10/26  Pesos  407,90  3/3</code>
/// <code>17/10/2026  •4822  MERPAGO*GER73XX  $  1.300,00  1/10</code>
/// La máscara de la tarjeta, el tipo ("Plan Pagos", "Comun") y la cuota son opcionales.
/// </summary>
public static partial class EstadoDeCuenta
{
    public const int MaxCaracteres = 30_000;
    public const int MaxLineas = 400;
    /// <summary>Un movimiento real ocupa menos de 150 caracteres; más que esto no es un movimiento.</summary>
    public const int MaxLargoLinea = 300;

    // Defensa contra ReDoS: líneas acotadas, espacios colapsados y tiempo máximo por línea.
    // Sin esto, una línea de 4.000 caracteres con muchos espacios tardaba más de un minuto.
    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    /// <summary>Máscara de la tarjeta: "**** 1012", "XXXX 1012" o "•4822" (opcional en los dos formatos).</summary>
    private const string Mascara = @"(?:(?:[*xX]{2,}|[•·]+)\s*\d{3,4}\s+)?";

    /// <summary>Moneda, monto y cuota: el final de la línea, igual en los dos formatos.</summary>
    private const string Final =
        @"(?<moneda>pesos|d[oó]lares|u\$s|us\$|usd|uyu|\$)\s*" +
        @"(?<monto>-?\s?\d[\d.,]*)" +
        @"(?:\s+(?<n>\d{1,2})\s*/\s*(?<N>\d{1,2}))?\s*$";

    // Formato 1: comercio, tipo y fecha.
    [GeneratedRegex(
        @"^\s*" + Mascara +
        @"(?<comercio>.+?)\s+" +
        @"(?:(?:plan\s+(?:de\s+)?pagos?|com[uú]n|contado|cuotas)\s+)?" +        // tipo (opcional)
        @"(?<fecha>\d{1,2}/\d{1,2}/(?:\d{4}|\d{2}))\s+" + Final,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Linea();

    // Formato 2 (otros bancos): la fecha primero, después la tarjeta y el comercio.
    [GeneratedRegex(
        @"^\s*(?<fecha>\d{1,2}/\d{1,2}/(?:\d{4}|\d{2}))\s+" + Mascara +
        @"(?<comercio>.+?)\s+" + Final,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LineaFechaPrimero();

    private static readonly string[] MarcasDePago = { "RECIBO DE PAGO", "PAGO RECIBIDO", "SU PAGO", "PAGO EN LINEA", "PAGO TARJETA" };

    public static List<LineaEstadoCuenta> Interpretar(string? texto)
    {
        var resultado = new List<LineaEstadoCuenta>();
        if (string.IsNullOrWhiteSpace(texto)) return resultado;
        if (texto.Length > MaxCaracteres) texto = texto[..MaxCaracteres];

        var lineas = texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int numero = 0;
        foreach (var cruda in lineas)
        {
            numero++;
            string linea = cruda.Replace(' ', ' ').Trim();
            if (linea.Length == 0) continue;
            if (resultado.Count >= MaxLineas) break;
            resultado.Add(InterpretarLinea(numero, linea));
        }
        return resultado;
    }

    public static LineaEstadoCuenta InterpretarLinea(int numero, string linea)
    {
        if (linea.Length > MaxLargoLinea)
            return new LineaEstadoCuenta(numero, linea[..80] + "…", TipoLinea.NoReconocida, Motivo: "Línea demasiado larga para ser un movimiento.");

        Match m;
        try
        {
            string limpia = Espacios().Replace(linea, " ").Trim();
            m = Linea().Match(limpia);
            if (!m.Success) m = LineaFechaPrimero().Match(limpia);
        }
        catch (RegexMatchTimeoutException)
        {
            return new LineaEstadoCuenta(numero, linea, TipoLinea.NoReconocida, Motivo: "No se pudo interpretar la línea.");
        }
        if (!m.Success)
            return new LineaEstadoCuenta(numero, linea, TipoLinea.NoReconocida, Motivo: "No tiene fecha, moneda y monto en el formato esperado.");

        string comercio = Regex.Replace(m.Groups["comercio"].Value, @"\s+", " ").Trim();

        if (!TryFecha(m.Groups["fecha"].Value, out var fecha))
            return new LineaEstadoCuenta(numero, linea, TipoLinea.NoReconocida, comercio, Motivo: "Fecha inválida.");
        if (!TryMonto(m.Groups["monto"].Value, out var monto))
            return new LineaEstadoCuenta(numero, linea, TipoLinea.NoReconocida, comercio, fecha, Motivo: "Monto inválido.");

        var moneda = LeerMoneda(m.Groups["moneda"].Value);
        int n = 1, total = 1;
        if (m.Groups["N"].Success)
        {
            n = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            total = int.Parse(m.Groups["N"].Value, CultureInfo.InvariantCulture);
            if (total < 1 || total > PlanDeCuotas.MaxCuotas || n < 1 || n > total)
                return new LineaEstadoCuenta(numero, linea, TipoLinea.NoReconocida, comercio, fecha, moneda, monto, Motivo: $"Cuota {n}/{total} inválida.");
        }

        bool esPago = MarcasDePago.Any(p => comercio.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (esPago || monto < 0)
            return new LineaEstadoCuenta(numero, linea, TipoLinea.PagoTarjeta, comercio, fecha, moneda, Math.Abs(monto), n, total,
                esPago ? "Pago de la tarjeta: no es un gasto." : "Monto negativo (devolución o crédito): no es un gasto.");
        if (monto == 0)
            return new LineaEstadoCuenta(numero, linea, TipoLinea.NoReconocida, comercio, fecha, moneda, monto, Motivo: "Monto en cero.");

        return new LineaEstadoCuenta(numero, linea, TipoLinea.Gasto, comercio, fecha, moneda, monto, n, total);
    }

    private static bool TryFecha(string s, out DateOnly fecha)
    {
        string[] formatos = { "d/M/yy", "d/M/yyyy" };
        return DateOnly.TryParseExact(s, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);
    }

    /// <summary>Acepta "1.830,00", "-15.189,15", "407,90", "1830.00" y "1830".</summary>
    public static bool TryMonto(string s, out decimal monto)
    {
        s = s.Replace(" ", "");
        string normal;
        if (s.Contains(','))
            normal = s.Replace(".", "").Replace(',', '.');                    // formato es-UY
        else if (Regex.IsMatch(s, @"^-?\d+\.\d{1,2}$"))
            normal = s;                                                       // 1830.00
        else
            normal = s.Replace(".", "");                                      // 1.830
        return decimal.TryParse(normal, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out monto);
    }

    private static Moneda LeerMoneda(string s) => s.ToLowerInvariant() switch
    {
        "pesos" or "uyu" or "$" => Moneda.UYU,
        _ => Moneda.USD,
    };

    /// <summary>Clave para comparar comercios: mayúsculas, sin tildes y con espacios simples.</summary>
    public static string Normalizar(string? comercio)
    {
        if (string.IsNullOrWhiteSpace(comercio)) return "";
        var s = comercio.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return Regex.Replace(sb.ToString(), @"\s+", " ");
    }

    private static readonly string[] Prefijos = { "MERPAGO*", "MERPAGO *", "DLO*", "DLO *", "PAYU*", "SQ *", "SQ*", "PAYPAL *", "PAYPAL*", "MP*", "EBANX*" };

    /// <summary>Descripción legible a partir del comercio: "MERPAGO*LACUEVAMUEBL" → "Lacuevamuebl".</summary>
    public static string DescripcionSugerida(string comercio)
    {
        string s = comercio.Trim();
        foreach (var p in Prefijos)
            if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { s = s[p.Length..].Trim(); break; }
        s = Regex.Replace(s, @"\.(COM|NET|ORG|UY)\b", "", RegexOptions.IgnoreCase);
        if (s.Length == 0) s = comercio.Trim();
        var ti = CultureInfo.GetCultureInfo("es-UY").TextInfo;
        string titulo = ti.ToTitleCase(s.ToLowerInvariant());
        return titulo.Length > 120 ? titulo[..120] : titulo;
    }

    /// <summary>Reglas de partida (comercio → nombre de categoría) cuando todavía no hay historial.</summary>
    public static readonly (string Contiene, string Categoria)[] ReglasBase =
    {
        ("PEDIDOSYA", "Delivery y comidas"), ("RAPPI", "Delivery y comidas"), ("MCDONALD", "Delivery y comidas"), ("BURGER", "Delivery y comidas"),
        ("NETFLIX", "Suscripciones"), ("SPOTIFY", "Suscripciones"), ("DISNEY", "Suscripciones"), ("HBO", "Suscripciones"), ("YOUTUBE", "Suscripciones"), ("APPLE.COM", "Suscripciones"), ("GOOGLE", "Suscripciones"), ("PRIME VIDEO", "Suscripciones"),
        ("ANTEL", "Servicios"), ("UTE", "Servicios"), ("OSE", "Servicios"), ("MOVISTAR", "Servicios"), ("CLARO", "Servicios"),
        ("DISA", "Transporte y combustible"), ("ANCAP", "Transporte y combustible"), ("AXION", "Transporte y combustible"), ("PETROBRAS", "Transporte y combustible"), ("UBER", "Transporte y combustible"), ("CABIFY", "Transporte y combustible"), ("STM", "Transporte y combustible"),
        ("MERCADOLIBRE", "Compras"), ("TIENDAMIA", "Compras"), ("AMAZON", "Compras"), ("TEMU", "Compras"), ("SHEIN", "Compras"),
        ("TKTX", "Entretenimiento"), ("TICKANTEL", "Entretenimiento"), ("REDTICKETS", "Entretenimiento"), ("MOVIE", "Entretenimiento"), ("STEAM", "Entretenimiento"),
        ("TATA", "Supermercado"), ("DISCO", "Supermercado"), ("DEVOTO", "Supermercado"), ("GEANT", "Supermercado"), ("TIENDA INGLESA", "Supermercado"), ("EL DORADO", "Supermercado"), ("MACRO MERCADO", "Supermercado"),
        ("FARMASHOP", "Salud"), ("SAN ROQUE", "Salud"), ("FARMACIA", "Salud"), ("NUEVA VISION", "Salud"),
        ("WORKANA", "Trabajo"), ("FIVERR", "Trabajo"), ("UPWORK", "Trabajo"),
    };
}
