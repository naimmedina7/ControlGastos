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

    /// <summary>
    /// Usa ConnectionStrings:Default o, si no está, la variable DATABASE_URL (formato
    /// postgres://usuario:clave@host:puerto/base que dan Render, Neon y otros).
    /// </summary>
    public static string ObtenerConexion(IConfiguration config)
    {
        string? cs = config.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(cs)) return cs;

        string? url = config["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Falta la cadena de conexión: definir ConnectionStrings__Default o DATABASE_URL.");
        return DesdeUrl(url);
    }

    public static string DesdeUrl(string url)
    {
        var uri = new Uri(url);
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
