using ControlGastos.Web.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ControlGastos.Web.Data;

/// <summary>
/// Contexto de la base. Aísla los datos por usuario en dos niveles:
/// 1. Filtro global en cada consulta: solo se leen filas con UserId = usuario actual.
/// 2. Al guardar: las filas nuevas toman el UserId del usuario actual, y se rechaza modificar o borrar filas ajenas.
/// Las clases <see cref="SqliteAppDbContext"/> y <see cref="PostgresAppDbContext"/> existen solo para tener
/// migraciones separadas por motor.
/// </summary>
public abstract class AppDbContext : IdentityDbContext<AppUser>, IDataProtectionKeyContext
{
    private readonly IUsuarioActual _usuario;

    protected AppDbContext(DbContextOptions options, IUsuarioActual usuario) : base(options)
    {
        _usuario = usuario;
    }

    /// <summary>Lo lee el filtro global; EF lo evalúa por instancia del contexto.</summary>
    public string? UsuarioActualId => _usuario.UserId;

    public DbSet<Tarjeta> Tarjetas => Set<Tarjeta>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Gasto> Gastos => Set<Gasto>();
    public DbSet<Cuota> Cuotas => Set<Cuota>();
    public DbSet<GastoRecurrente> GastosRecurrentes => Set<GastoRecurrente>();
    public DbSet<TipoCambio> TiposCambio => Set<TipoCambio>();
    public DbSet<Presupuesto> Presupuestos => Set<Presupuesto>();
    public DbSet<Ingreso> Ingresos => Set<Ingreso>();
    /// <summary>Datos públicos compartidos (sin filtro por usuario).</summary>
    public DbSet<CotizacionBcu> CotizacionesBcu => Set<CotizacionBcu>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// Todas las fechas con hora (DateTime) se guardan y se leen en UTC. PostgreSQL rechaza fechas sin zona
    /// ("Kind=Unspecified"), que pueden llegar de un respaldo exportado desde SQLite; SQLite, a su vez, las
    /// devuelve sin zona. Con este conversor, los dos motores se comportan igual.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<FechaUtcConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<FechaUtcNullableConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>(e => e.Property(u => u.Nombre).HasMaxLength(80));

        b.Entity<Tarjeta>(e =>
        {
            e.Property(t => t.Nombre).HasMaxLength(60).IsRequired();
            e.Property(t => t.Color).HasMaxLength(7);
            e.HasIndex(t => new { t.UserId, t.Nombre }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(t => t.CierreEfectivo);
            e.HasQueryFilter(t => t.UserId == UsuarioActualId);
        });

        b.Entity<Categoria>(e =>
        {
            e.Property(c => c.Nombre).HasMaxLength(60).IsRequired();
            e.Property(c => c.Icono).HasMaxLength(40);
            e.Property(c => c.Color).HasMaxLength(7);
            e.HasIndex(c => new { c.UserId, c.Nombre }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(c => c.UserId == UsuarioActualId);
        });

        b.Entity<Gasto>(e =>
        {
            e.Property(g => g.Descripcion).HasMaxLength(120).IsRequired();
            e.Property(g => g.Comercio).HasMaxLength(120);
            e.Property(g => g.Notas).HasMaxLength(500);
            e.Property(g => g.Moneda).HasConversion<string>().HasMaxLength(3);
            e.Property(g => g.MontoCuota).HasPrecision(14, 2);
            e.HasIndex(g => new { g.UserId, g.Fecha });
            e.HasOne<AppUser>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
            // Una tarjeta con gastos no se borra: se desactiva.
            e.HasOne(g => g.Tarjeta).WithMany(t => t.Gastos).HasForeignKey(g => g.TarjetaId).OnDelete(DeleteBehavior.Restrict);
            // Si se borra una categoría, sus gastos quedan "sin categoría".
            e.HasOne(g => g.Categoria).WithMany(c => c.Gastos).HasForeignKey(g => g.CategoriaId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(g => g.Cuotas).WithOne(c => c.Gasto).HasForeignKey(c => c.GastoId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(g => g.MontoTotal);
            e.Ignore(g => g.EnCuotas);
            e.Ignore(g => g.UltimoPeriodo);
            e.Ignore(g => g.MedioDePago);
            e.Ignore(g => g.EsFijo);
            // Si se borra el recurrente, los gastos ya generados se conservan.
            e.HasOne(g => g.GastoRecurrente).WithMany(r => r.Gastos).HasForeignKey(g => g.GastoRecurrenteId).OnDelete(DeleteBehavior.SetNull);
            // Un recurrente genera un solo gasto por mes (los NULL no chocan entre sí).
            e.HasIndex(g => new { g.GastoRecurrenteId, g.PrimerPeriodo }).IsUnique();
            e.HasQueryFilter(g => g.UserId == UsuarioActualId);
        });

        b.Entity<GastoRecurrente>(e =>
        {
            e.ToTable("GastosRecurrentes");
            e.Property(r => r.Descripcion).HasMaxLength(120).IsRequired();
            e.Property(r => r.Notas).HasMaxLength(500);
            e.Property(r => r.Moneda).HasConversion<string>().HasMaxLength(3);
            e.Property(r => r.Monto).HasPrecision(14, 2);
            e.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Tarjeta).WithMany().HasForeignKey(r => r.TarjetaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Categoria).WithMany().HasForeignKey(r => r.CategoriaId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(r => new { r.UserId, r.Activo });
            e.Ignore(r => r.MedioDePago);
            e.HasQueryFilter(r => r.UserId == UsuarioActualId);
        });

        b.Entity<Cuota>(e =>
        {
            e.Property(c => c.Monto).HasPrecision(14, 2);
            e.HasIndex(c => new { c.UserId, c.Periodo });
            e.HasIndex(c => new { c.GastoId, c.Numero }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(c => c.UserId == UsuarioActualId);
        });

        b.Entity<Tarjeta>().Property(t => t.Tipo).HasConversion<string>().HasMaxLength(10);

        b.Entity<TipoCambio>(e =>
        {
            e.ToTable("TiposCambio");
            e.Property(t => t.UsdEnPesos).HasPrecision(12, 4);
            e.HasIndex(t => new { t.UserId, t.Mes }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(t => t.UserId == UsuarioActualId);
        });

        b.Entity<CotizacionBcu>(e =>
        {
            e.ToTable("CotizacionesBcu");
            e.HasKey(c => c.Mes);
            e.Property(c => c.Mes).ValueGeneratedNever();
            e.Property(c => c.UsdEnPesos).HasPrecision(12, 4);
        });

        b.Entity<Presupuesto>(e =>
        {
            e.Property(p => p.Moneda).HasConversion<string>().HasMaxLength(3);
            e.Property(p => p.MontoMensual).HasPrecision(14, 2);
            e.HasIndex(p => new { p.UserId, p.CategoriaId, p.Moneda }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            // Si se borra la categoría, su presupuesto deja de tener sentido.
            e.HasOne(p => p.Categoria).WithMany().HasForeignKey(p => p.CategoriaId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(p => p.UserId == UsuarioActualId);
        });

        b.Entity<Ingreso>(e =>
        {
            e.Property(i => i.Descripcion).HasMaxLength(120).IsRequired();
            e.Property(i => i.Notas).HasMaxLength(500);
            e.Property(i => i.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Moneda).HasConversion<string>().HasMaxLength(3);
            e.Property(i => i.Monto).HasPrecision(14, 2);
            e.HasIndex(i => new { i.UserId, i.Fecha });
            e.HasOne<AppUser>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(i => i.UserId == UsuarioActualId);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AplicarDueño();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AplicarDueño();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void AplicarDueño()
    {
        string? actual = UsuarioActualId;
        foreach (var entry in ChangeTracker.Entries<IPerteneceAUsuario>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (string.IsNullOrEmpty(entry.Entity.UserId))
                        entry.Entity.UserId = actual ?? throw new InvalidOperationException("No hay un usuario para asignar como dueño del dato.");
                    else if (actual != null && entry.Entity.UserId != actual)
                        throw new InvalidOperationException("No se puede crear un dato a nombre de otro usuario.");
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    // Se compara contra el valor original: cambiar el UserId de una fila tampoco está permitido.
                    var original = (string?)entry.Property(nameof(IPerteneceAUsuario.UserId)).OriginalValue;
                    if (original != actual || entry.Entity.UserId != actual)
                        throw new InvalidOperationException("No se puede modificar un dato de otro usuario.");
                    break;
            }
        }
    }
}

public class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options, IUsuarioActual usuario)
    : AppDbContext(options, usuario);

public class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options, IUsuarioActual usuario)
    : AppDbContext(options, usuario);

/// <summary>Normaliza a UTC al guardar (una fecha sin zona se toma como UTC) y marca como UTC al leer.</summary>
public class FechaUtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
    v => AUtc(v),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
    public static DateTime AUtc(DateTime v) => v.Kind switch
    {
        DateTimeKind.Utc => v,
        DateTimeKind.Local => v.ToUniversalTime(),
        _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
    };
}

public class FechaUtcNullableConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
    v => v.HasValue ? FechaUtcConverter.AUtc(v.Value) : v,
    v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
