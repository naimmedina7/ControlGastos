using System.Security.Cryptography;
using ControlGastos.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Infrastructure;

/// <summary>
/// Comandos de administración por consola, para cuando no hay email (por ejemplo, alguien olvidó su contraseña).
/// Se ejecutan desde tu PC apuntando a la base de producción; no levantan el servidor web:
/// <code>dotnet run --project src/ControlGastos.Web -- admin reset-password correo@ejemplo.com</code>
/// </summary>
public static class ComandosAdmin
{
    public static bool EsComando(string[] args) => args.Length > 0 && args[0].Equals("admin", StringComparison.OrdinalIgnoreCase);

    private const string Ayuda = """
        Comandos disponibles:
          admin usuarios                  Lista las cuentas (correo, alta, si está bloqueada).
          admin reset-password <correo>   Genera una contraseña temporal, desbloquea la cuenta y cierra sus sesiones.
          admin desbloquear <correo>      Quita el bloqueo por intentos fallidos.
        """;

    public static async Task<int> EjecutarAsync(IServiceProvider services, string[] args, TextWriter salida)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        string comando = args.Length > 1 ? args[1].ToLowerInvariant() : "";
        string? email = args.Length > 2 ? args[2].Trim() : null;

        switch (comando)
        {
            case "usuarios":
                var lista = await users.Users.OrderBy(u => u.CreadoEn).ToListAsync();
                foreach (var u in lista)
                {
                    bool bloqueada = u.LockoutEnd > DateTimeOffset.UtcNow;
                    salida.WriteLine($"{u.Email,-40} alta {u.CreadoEn:yyyy-MM-dd}{(bloqueada ? "  [bloqueada]" : "")}");
                }
                salida.WriteLine($"{lista.Count} cuenta(s).");
                return 0;

            case "reset-password" when email != null:
            {
                var user = await users.FindByEmailAsync(email);
                if (user == null) { salida.WriteLine("No existe una cuenta con ese correo."); return 1; }

                string temporal = GenerarTemporal();
                var token = await users.GeneratePasswordResetTokenAsync(user);
                var r = await users.ResetPasswordAsync(user, token, temporal); // también cambia el security stamp
                if (!r.Succeeded)
                {
                    salida.WriteLine("No se pudo cambiar: " + string.Join("; ", r.Errors.Select(e => e.Description)));
                    return 1;
                }
                await users.SetLockoutEndDateAsync(user, null);
                await users.ResetAccessFailedCountAsync(user);

                salida.WriteLine($"Contraseña temporal de {user.Email}: {temporal}");
                salida.WriteLine("Pásasela por un canal privado y pídele que la cambie en Mi perfil apenas entre.");
                salida.WriteLine("Las sesiones abiertas de esa cuenta se cierran en los próximos minutos.");
                return 0;
            }

            case "desbloquear" when email != null:
            {
                var user = await users.FindByEmailAsync(email);
                if (user == null) { salida.WriteLine("No existe una cuenta con ese correo."); return 1; }
                await users.SetLockoutEndDateAsync(user, null);
                await users.ResetAccessFailedCountAsync(user);
                salida.WriteLine($"{user.Email} desbloqueada.");
                return 0;
            }

            default:
                salida.WriteLine(Ayuda);
                return comando.Length == 0 || comando == "ayuda" ? 0 : 1;
        }
    }

    /// <summary>16 caracteres aleatorios (generador criptográfico), sin caracteres confusos, en grupos de 4.</summary>
    public static string GenerarTemporal()
    {
        const string letras = "abcdefghjkmnpqrstuvwxyz";
        const string digitos = "23456789";
        while (true)
        {
            string s = RandomNumberGenerator.GetString(letras + digitos, 16);
            if (s.Any(char.IsDigit) && s.Any(char.IsLower))
                return string.Join('-', s.Chunk(4).Select(c => new string(c)));
        }
    }
}
