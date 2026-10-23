namespace ControlGastos.Web.Domain;

/// <summary>Categorías que recibe cada usuario nuevo. Después las puede editar o borrar.</summary>
public static class CategoriasPorDefecto
{
    public static readonly (string Nombre, string Icono, string Color)[] Lista =
    {
        ("Supermercado", "bi-cart", "#16a34a"),
        ("Delivery y comidas", "bi-cup-hot", "#ea580c"),
        ("Transporte y combustible", "bi-fuel-pump", "#0284c7"),
        ("Hogar", "bi-house", "#a16207"),
        ("Salud", "bi-heart-pulse", "#dc2626"),
        ("Educación", "bi-mortarboard", "#7c3aed"),
        ("Suscripciones", "bi-play-btn", "#db2777"),
        ("Servicios", "bi-lightning-charge", "#0891b2"),
        ("Tecnología", "bi-laptop", "#4f46e5"),
        ("Compras", "bi-bag", "#c026d3"),
        ("Entretenimiento", "bi-ticket-perforated", "#e11d48"),
        ("Préstamos", "bi-bank", "#475569"),
        ("Trabajo", "bi-briefcase", "#0f766e"),
        ("Otros", "bi-three-dots", "#64748b"),
    };

    public static IEnumerable<Categoria> Crear(string userId) =>
        Lista.Select(c => new Categoria { UserId = userId, Nombre = c.Nombre, Icono = c.Icono, Color = c.Color });
}
