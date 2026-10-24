using ControlGastos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Infrastructure;

/// <summary>Elige el motor de base según la configuración "Database:Provider" (Sqlite en desarrollo, Postgres en producción).</summary>
public static class ConfiguracionBase
{
    public static IServiceCollection AddBaseDeDatos(this IServiceCollection services, IConfiguration config, string contentRoot)
    {
        string proveedor = config["Database:Provider"] ?? "Sqlite";
        string conexion = ObtenerConexion(config);

        if (proveedor.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            services.AddDbContext<AppDbContext, PostgresAppDbContext>(o => o.UseNpgsql(conexion));
        else if (proveedor.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            services.AddDbContext<AppDbContext, SqliteAppDbContext>(o => o.UseSqlite(RutaSqlite(conexion, contentRoot)));
        else
            throw new InvalidOperationException($"Database:Provider desconocido: '{proveedor}'. Usar Sqlite o Postgres.");

        return services;
    }

    /// <summary>Una ruta relativa de SQLite se resuelve contra la carpeta del proyecto, no contra el directorio desde donde se ejecuta.</summary>
    private static string RutaSqlite(string conexion, string contentRoot)
    {
        var b = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(conexion);
        if (b.DataSource != ":memory:" && !b.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(b.DataSource))
        {
            b.DataSource = Path.Combine(contentRoot, b.DataSource);
            Directory.CreateDirectory(Path.GetDirectoryName(b.DataSource)!);
        }
        return b.ConnectionString;
    }

    /// <summary>Variables que usan Neon, la integración de Vercel y otros hostings, en orden de preferencia.</summary>
    private static readonly string[] VariablesConocidas =
        { "DATABASE_URL", "POSTGRES_URL", "DATABASE_URL_UNPOOLED", "POSTGRES_URL_NON_POOLING" };

    /// <summary>
    /// Usa ConnectionStrings:Default (formato Npgsql o URL) o, si no está, la URL de Postgres que inyecte el hosting
    /// (DATABASE_URL, POSTGRES_URL…, también con prefijo, por ejemplo NEON_DATABASE_URL).
    /// </summary>
    public static string ObtenerConexion(IConfiguration config)
    {
        string? cs = config.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(cs)) return EsUrl(cs) ? DesdeUrl(cs) : cs;

        foreach (var nombre in VariablesConocidas)
            if (config[nombre] is { Length: > 0 } url) return DesdeUrl(url);

        // Integraciones con prefijo personalizado: XXX_DATABASE_URL o XXX_POSTGRES_URL.
        var claves = config.AsEnumerable().Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
        var conPrefijo = claves.FirstOrDefault(k => k.EndsWith("_DATABASE_URL", StringComparison.OrdinalIgnoreCase)
                                                 || k.EndsWith("_POSTGRES_URL", StringComparison.OrdinalIgnoreCase));
        if (conPrefijo != null) return DesdeUrl(config[conPrefijo]!);

        // Para diagnosticar sin exponer secretos: solo los NOMBRES de variables que parecen de base de datos.
        var vistas = claves.Where(k => k.Contains("DATABASE", StringComparison.OrdinalIgnoreCase)
                                    || k.Contains("POSTGRES", StringComparison.OrdinalIgnoreCase)
                                    || k.StartsWith("PG", StringComparison.OrdinalIgnoreCase))
                           .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        throw new InvalidOperationException(
            "Falta la cadena de conexión: definir DATABASE_URL (la agrega Neon desde Vercel → Storage, para el entorno Production; " +
            "después hay que hacer Redeploy) o ConnectionStrings__Default. " +
            (vistas.Count > 0 ? "Variables de base encontradas (solo nombres): " + string.Join(", ", vistas) + "."
                              : "No se encontró ninguna variable de base de datos."));
    }

    private static bool EsUrl(string s) =>
        s.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    public static string DesdeUrl(string url)
    {
        if (!EsUrl(url.Trim()))
            throw new InvalidOperationException("La URL de la base debe empezar con postgres:// o postgresql://.");
        var uri = new Uri(url.Trim());
        var userInfo = uri.UserInfo.Split(':', 2);
        var b = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = Npgsql.SslMode.Require,
        };
        return b.ConnectionString;
    }
}
