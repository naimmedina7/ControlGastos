using System.Globalization;
using System.Net;
using ControlGastos.Web.Domain;
using Microsoft.AspNetCore.Html;

namespace ControlGastos.Web.Infrastructure;

/// <summary>Formato visible en es-UY (montos, fechas, meses) y piezas chicas de UI. Adaptado de gastos-equipos.</summary>
public static class Fmt
{
    private static readonly NumberFormatInfo Num = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberDecimalDigits = 2,
    };

    private static readonly string[] Meses = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
    private static readonly string[] MesesCortos = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };

    public static string Simbolo(Moneda m) => m == Moneda.USD ? "US$" : "$";

    public static string Money(decimal v, Moneda m = Moneda.UYU) => Simbolo(m) + " " + v.ToString("N2", Num);

    public static string MoneyShort(decimal v, Moneda m = Moneda.UYU)
    {
        string s = Simbolo(m) + " ";
        if (Math.Abs(v) >= 1_000_000) return s + (v / 1_000_000).ToString("0.#", Num) + " M";
        if (Math.Abs(v) >= 10_000) return s + (v / 1000).ToString("0", Num) + " k";
        return s + v.ToString("N0", Num);
    }

    /// <summary>Monto grande con centavos atenuados (para KPIs).</summary>
    public static IHtmlContent MoneyHtml(decimal v, Moneda m = Moneda.UYU)
    {
        string s = v.ToString("N2", Num);
        int i = s.LastIndexOf(',');
        return new HtmlString($"<span class=\"cur\">{Simbolo(m)}</span>{s[..i]}<span class=\"cents\">{s[i..]}</span>");
    }

    public static string Fecha(DateOnly d) => $"{d.Day:00} {MesesCortos[d.Month - 1]} {d.Year}";
    public static string FechaCorta(DateOnly d) => $"{d.Day:00} {MesesCortos[d.Month - 1]}";
    public static string MesAnio(DateOnly d) => $"{Meses[d.Month - 1]} {d.Year}";
    public static string MesAnioCap(DateOnly d) => Cap(MesAnio(d));
    public static string MesCorto(DateOnly d) => Cap(MesesCortos[d.Month - 1]);
    public static string MesCortoAnio(DateOnly d) => $"{MesesCortos[d.Month - 1]} {d.Year}";
    public static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    /// <summary>Valor del parámetro ?mes= (yyyy-MM).</summary>
    public static string MesParam(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static DateOnly? LeerMes(string? valor) =>
        DateOnly.TryParseExact((valor ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string Tipo(TipoTarjeta t) => t == TipoTarjeta.Credito ? "Crédito" : "Débito";

    public static string Tipo(TipoIngreso t) => t switch
    {
        TipoIngreso.Sueldo => "Sueldo",
        TipoIngreso.Venta => "Venta",
        TipoIngreso.PrestamoDevuelto => "Préstamo devuelto",
        _ => "Otro ingreso",
    };

    public static string Icono(TipoIngreso t) => t switch
    {
        TipoIngreso.Sueldo => "bi-briefcase",
        TipoIngreso.Venta => "bi-bag-check",
        TipoIngreso.PrestamoDevuelto => "bi-arrow-left-right",
        _ => "bi-plus-circle",
    };

    public static IHtmlContent TipoBadge(Tarjeta? t)
    {
        if (t == null) return new HtmlString("<span class=\"chip chip-efectivo\"><i class=\"bi bi-cash-stack\" aria-hidden=\"true\"></i>Efectivo</span>");
        return t.Tipo == TipoTarjeta.Credito
            ? new HtmlString("<span class=\"chip chip-credito\"><i class=\"bi bi-credit-card\" aria-hidden=\"true\"></i>Crédito</span>")
            : new HtmlString("<span class=\"chip chip-debito\"><i class=\"bi bi-credit-card-2-front\" aria-hidden=\"true\"></i>Débito</span>");
    }

    public static IHtmlContent MonedaBadge(Moneda m) => m == Moneda.USD
        ? new HtmlString("<span class=\"chip chip-usd\">USD</span>")
        : HtmlString.Empty;

    /// <summary>Punto de color de una tarjeta o categoría (el color se valida al guardar: #RRGGBB).</summary>
    public static IHtmlContent Punto(string? color) =>
        new HtmlString($"<span class=\"dot-color\" style=\"background:{WebUtility.HtmlEncode(ColorSeguro(color))}\" aria-hidden=\"true\"></span>");

    /// <summary>Color de "Sin categoría": gris pizarra (el blanco encima da 4,8:1).</summary>
    public const string ColorSinCategoria = "#64748b";

    /// <summary>Ícono de categoría: fondo sólido con su color e ícono blanco.</summary>
    public static IHtmlContent IconoCategoria(Categoria? c, string extra = "") =>
        IconoGrupo(c?.Icono ?? "bi-question-circle", c?.Color ?? ColorSinCategoria, extra);

    public static IHtmlContent IconoGrupo(string? icono, string color, string extra = "")
    {
        color = ColorSeguro(color);
        return new HtmlString($"<span class=\"cat-icon {extra}\" style=\"background:{color};color:{ColorIconoSobre(color)}\" aria-hidden=\"true\"><i class=\"bi {WebUtility.HtmlEncode(icono ?? "bi-credit-card")}\"></i></span>");
    }

    /// <summary>Avance de una compra en cuotas: un segmento por cuota (hasta 12); con más, una barra continua.
    /// Es decorativo: el texto de al lado ya dice cuántas faltan.</summary>
    public static IHtmlContent ProgresoCuotas(int hechas, int total)
    {
        if (total < 2) return HtmlString.Empty;
        hechas = Math.Clamp(hechas, 0, total);
        var sb = new System.Text.StringBuilder("<span class=\"cuotas-avance\" aria-hidden=\"true\">");
        if (total <= 12)
            for (int i = 1; i <= total; i++) sb.Append(i <= hechas ? "<i class=\"on\"></i>" : "<i></i>");
        else
            sb.Append($"<i class=\"barra\"><b style=\"width:{hechas * 100 / total}%\"></b></i>");
        return new HtmlString(sb.Append("</span>").ToString());
    }

    /// <summary>Blanco sobre el color, salvo que no llegue a 3:1 (colores claros de un respaldo): ahí, verde tinta.</summary>
    public static string ColorIconoSobre(string color)
    {
        double l = Luminancia(ColorSeguro(color));
        return 1.05 / (l + 0.05) >= 3 ? "#fff" : "#14221c";
    }

    private static double Luminancia(string hex)
    {
        double Canal(int i)
        {
            double v = Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Canal(1) + 0.7152 * Canal(3) + 0.0722 * Canal(5);
    }

    public static bool EsColorValido(string? c) =>
        c != null && c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit);

    public static string ColorSeguro(string? c) => EsColorValido(c) ? c! : "#64748b";

    public static string Iniciales(string? nombre)
    {
        var partes = (nombre ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length switch
        {
            0 => "?",
            1 => partes[0][..1].ToUpperInvariant(),
            _ => (partes[0][..1] + partes[^1][..1]).ToUpperInvariant(),
        };
    }

    /// <summary>Escala de eje "redonda": devuelve el paso y la cantidad de divisiones.</summary>
    public static (decimal step, int lines) NiceScale(decimal max)
    {
        if (max <= 0) return (1000, 4);
        double raw = (double)max / 4;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double[] ladder = { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 };
        double step = ladder.First(l => l * mag >= raw) * mag;
        int lines = (int)Math.Ceiling((double)max / step);
        if ((double)max / (step * lines) > 0.92) lines++;
        return ((decimal)step, Math.Clamp(lines, 3, 6));
    }

    /// <summary>Texto normalizado para búsqueda en el cliente (minúsculas, sin tildes).</summary>
    public static string SearchKey(params string?[] partes)
    {
        string s = string.Join(" ", partes.Where(p => !string.IsNullOrEmpty(p))).ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Porcentaje con punto decimal, para usar en CSS (width).</summary>
    public static string Pct(decimal parte, decimal total) =>
        total <= 0 ? "0" : (parte / total * 100).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Porcentaje para mostrar al usuario: "32,5".</summary>
    public static string PctTexto(decimal parte, decimal total) =>
        total <= 0 ? "0" : (parte / total * 100).ToString("0.#", Num);

    public static string Saludo(DateTime ahora) => ahora.Hour switch
    {
        < 12 => "Buenos días",
        < 20 => "Buenas tardes",
        _ => "Buenas noches",
    };
}
