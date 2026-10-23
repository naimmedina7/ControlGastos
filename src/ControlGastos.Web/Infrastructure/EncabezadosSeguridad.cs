namespace ControlGastos.Web.Infrastructure;

/// <summary>Encabezados HTTP de seguridad que se agregan a todas las respuestas.</summary>
public static class EncabezadosSeguridad
{
    // Solo se permiten scripts propios y de jsDelivr (Chart.js, íconos); nada de scripts inline.
    private const string Csp =
        "default-src 'self'; " +
        "script-src 'self' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net; " +
        "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'";

    public static IApplicationBuilder UseEncabezadosSeguridad(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h["Content-Security-Policy"] = Csp;
            await next();
        });
}
