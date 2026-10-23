using System.Globalization;
using System.Text;
using ControlGastos.Web.Domain;

namespace ControlGastos.Web.Infrastructure;

/// <summary>
/// CSV para abrir en Excel o Google Sheets con configuración regional de Uruguay:
/// separador ";", coma decimal y UTF-8 con BOM (para que Excel lea bien las tildes).
/// </summary>
public static class ExportadorCsv
{
    private static readonly string[] Encabezados =
        { "Mes", "Fecha", "Descripción", "Comercio", "Categoría", "Medio de pago", "Tipo", "Cuota", "Moneda", "Monto" };

    private static readonly NumberFormatInfo Num = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "" };

    public static byte[] Generar(IEnumerable<Cuota> cuotas)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', Encabezados));
        foreach (var c in cuotas)
        {
            var g = c.Gasto;
            string tipo = g.EsFijo ? "Fijo" : g.EnCuotas ? "Cuotas" : "Contado";
            sb.AppendLine(string.Join(';',
                Campo(c.Periodo.ToString("yyyy-MM", CultureInfo.InvariantCulture)),
                Campo(g.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                Campo(g.Descripcion),
                Campo(g.Comercio),
                Campo(g.Categoria?.Nombre ?? "Sin categoría"),
                Campo(g.MedioDePago),
                Campo(tipo),
                Campo(g.EnCuotas ? $"{c.Numero}/{g.CantidadCuotas}" : ""),
                Campo(g.Moneda.ToString()),
                c.Monto.ToString("0.00", Num)));
        }
        var bom = Encoding.UTF8.GetPreamble();
        return bom.Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>
    /// Escapa un texto para CSV. Además neutraliza la "inyección de fórmulas": un texto que empieza con
    /// = + - @ (o tabulador) se prefija con ' para que Excel no lo ejecute como fórmula.
    /// </summary>
    public static string Campo(string? valor)
    {
        string v = valor ?? "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        bool comillas = v.Contains(';') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
        return comillas ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
