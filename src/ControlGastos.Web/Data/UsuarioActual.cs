using System.Security.Claims;

namespace ControlGastos.Web.Data;

/// <summary>Id del usuario dueño de los datos en esta operación. Null = nadie (no se ve ningún dato).</summary>
public interface IUsuarioActual
{
    string? UserId { get; }
}

/// <summary>Toma el usuario del token (cookie) de la request en curso.</summary>
public class UsuarioActualHttp(IHttpContextAccessor accessor) : IUsuarioActual
{
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}

/// <summary>Usuario fijo, para tareas sin request (datos de ejemplo, tests).</summary>
public class UsuarioFijo(string? userId) : IUsuarioActual
{
    public string? UserId { get; } = userId;
}
