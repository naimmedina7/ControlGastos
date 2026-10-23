using System.Globalization;
using ControlGastos.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Data;

/// <summary>
/// Solo en desarrollo: crea una cuenta de prueba con los gastos del estado de cuenta de octubre 2026.
/// Se activa con la sección "DatosDeEjemplo" de appsettings.Development.json.
/// </summary>
public static class DatosDeEjemplo
{
    // Descripción, comercio, fecha, moneda, monto de la cuota, "n/N" (vacío = contado), categoría.
    private static readonly (string Desc, string Comercio, string Fecha, Moneda Moneda, decimal Monto, string Cuotas, string Categoria)[] Octubre =
    {
        ("Mercado Libre", "MERPAGO*MERCADOLIBRE", "05/10/26", Moneda.UYU, 407.90m, "3/3", "Compras"),
        ("Nueva Visión", "NUEVA VISION", "06/10/26", Moneda.UYU, 1830.00m, "5/6", "Salud"),
        ("SEP", "MERPAGO*SEP", "08/10/26", Moneda.UYU, 409.37m, "1/10", "Otros"),
        ("La Oferta", "MERPAGO*LAOFERTAIRRE", "08/10/26", Moneda.UYU, 2702.42m, "9/10", "Compras"),
        ("Thot Computación", "THOT COMPUTACION", "09/10/26", Moneda.USD, 22.00m, "6/6", "Tecnología"),
        ("Workana", "WORKANA LLC", "10/10/26", Moneda.USD, 10.27m, "", "Trabajo"),
        ("Sampler", "MERPAGO*SAMPLER", "10/10/26", Moneda.UYU, 633.21m, "5/6", "Compras"),
        ("PedidosYa · hamburguesa", "DLO*PedidosYa Burger", "11/10/26", Moneda.UYU, 1398.61m, "", "Delivery y comidas"),
        ("PedidosYa · envío", "DLO*PedidosYa Envio", "11/10/26", Moneda.UYU, 33.44m, "", "Delivery y comidas"),
        ("PedidosYa · propina", "DLO*PedidosYa Propin", "12/10/26", Moneda.UYU, 40.00m, "", "Delivery y comidas"),
        ("Antel", "ANTEL EN LINEA", "13/10/26", Moneda.UYU, 65.00m, "", "Servicios"),
        ("Mercado Libre", "MERPAGO*MERCADOLIBRE", "13/10/26", Moneda.UYU, 877.00m, "1/6", "Compras"),
        ("Victor", "MERPAGO*VICTOR", "14/10/26", Moneda.UYU, 549.00m, "2/10", "Otros"),
        ("Mercado Libre", "MERPAGO*MERCADOLIBRE", "14/10/26", Moneda.UYU, 664.60m, "1/5", "Compras"),
        ("Disa Los Pinos", "DISA LOS PINOS", "16/10/26", Moneda.UYU, 500.00m, "", "Transporte y combustible"),
        ("Dagheros", "MERPAGO*DAGHEROS", "17/10/26", Moneda.UYU, 385.93m, "2/3", "Otros"),
        ("Entradas (TKTX)", "MERPAGO*TKTX", "19/10/26", Moneda.UYU, 740.00m, "", "Entretenimiento"),
        ("La Cueva Muebles", "MERPAGO*LACUEVAMUEBL", "21/10/26", Moneda.UYU, 998.33m, "1/6", "Hogar"),
        ("Netflix", "NETFLIX.COM", "21/10/26", Moneda.USD, 5.17m, "", "Suscripciones"),
        ("Tiendamia", "DLO*Tiendamia UY - C", "25/10/26", Moneda.UYU, 1215.90m, "2/12", "Compras"),
        ("Finish", "MERPAGO*FINISH", "25/10/26", Moneda.UYU, 482.77m, "3/10", "Otros"),
    };

    public static async Task CargarAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        string? email = config["DatosDeEjemplo:Email"];
        string? password = config["DatosDeEjemplo:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync(email) != null) return;

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, Nombre = "Cuenta de prueba" };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogWarning("No se pudo crear la cuenta de ejemplo: {Errores}", string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        // Contexto propio "actuando como" el usuario nuevo, para que el aislamiento se aplique igual que en una request.
        await using var db = CrearContexto(scope.ServiceProvider, user.Id);

        db.Categorias.AddRange(CategoriasPorDefecto.Crear(user.Id));
        var credito = new Tarjeta { Nombre = "Visa crédito", Tipo = TipoTarjeta.Credito, Color = "#1d4ed8" };
        db.Tarjetas.Add(credito);
        db.Tarjetas.Add(new Tarjeta { Nombre = "Itaú débito", Tipo = TipoTarjeta.Debito, Color = "#ea580c" });
        await db.SaveChangesAsync();

        var categorias = await db.Categorias.ToDictionaryAsync(c => c.Nombre, c => c.Id);
        foreach (var g in Octubre)
        {
            var fecha = DateOnly.ParseExact(g.Fecha, "dd/MM/yy", CultureInfo.InvariantCulture);
            var (actual, total) = LeerCuotas(g.Cuotas);
            var gasto = new Gasto
            {
                Descripcion = g.Desc, Comercio = g.Comercio, Fecha = fecha, TarjetaId = credito.Id,
                CategoriaId = categorias[g.Categoria], Moneda = g.Moneda, MontoCuota = g.Monto,
                CantidadCuotas = total, PrimerPeriodo = PlanDeCuotas.PrimerPeriodo(fecha, actual),
            };
            gasto.Cuotas = PlanDeCuotas.Generar(gasto);
            db.Gastos.Add(gasto);
        }

        await db.SaveChangesAsync();

        // Gastos fijos que se pagan por transferencia: se cargan solos cada mes desde octubre 2026.
        var octubre = new DateOnly(2026, 10, 1);
        db.GastosRecurrentes.AddRange(
            new GastoRecurrente { Descripcion = "Préstamo moto", Moneda = Moneda.UYU, Monto = 5000m, DiaDelMes = 1, Desde = octubre, CategoriaId = categorias["Préstamos"] },
            new GastoRecurrente { Descripcion = "Facultad", Moneda = Moneda.UYU, Monto = 9000m, DiaDelMes = 1, Desde = octubre, CategoriaId = categorias["Educación"] });
        await db.SaveChangesAsync();
        var reloj = scope.ServiceProvider.GetRequiredService<Services.Reloj>();
        await new Services.RecurrenteService(db).GenerarPendientesAsync(reloj.MesActual);

        logger.LogInformation("Cuenta de ejemplo creada: {Email}", email);
    }

    private static AppDbContext CrearContexto(IServiceProvider sp, string userId)
    {
        var dueño = new UsuarioFijo(userId);
        return sp.GetRequiredService<AppDbContext>() switch
        {
            SqliteAppDbContext => new SqliteAppDbContext(sp.GetRequiredService<DbContextOptions<SqliteAppDbContext>>(), dueño),
            PostgresAppDbContext => new PostgresAppDbContext(sp.GetRequiredService<DbContextOptions<PostgresAppDbContext>>(), dueño),
            _ => throw new InvalidOperationException("Proveedor de base no soportado."),
        };
    }

    private static (int Actual, int Total) LeerCuotas(string s)
    {
        if (string.IsNullOrEmpty(s)) return (1, 1);
        var partes = s.Split('/');
        return (int.Parse(partes[0]), int.Parse(partes[1]));
    }
}
