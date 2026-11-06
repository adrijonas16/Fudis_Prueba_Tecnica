using Fudis.Pagos.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fudis.Pagos.Api.Database;

/// <summary>
/// La conexión de Entity Framework con SQL Server.
///
/// Las clases están en español, pero las tablas y columnas de la base de datos están en inglés
/// (las tablas Orders y DriverEarnings ya existen en producción y no se pueden renombrar).
/// Aquí se dice qué propiedad va con qué columna.
///
/// La estructura de las tablas la crean los scripts de db/migrations. Este archivo solo la describe.
/// </summary>
public sealed class FudisDbContext : DbContext
{
    public FudisDbContext(DbContextOptions<FudisDbContext> opciones) : base(opciones) { }

    /// <summary>Tabla Orders.</summary>
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    /// <summary>Tabla DriverEarnings.</summary>
    public DbSet<GananciaRepartidor> GananciasRepartidor => Set<GananciaRepartidor>();
    /// <summary>Tabla PaymentNotifications.</summary>
    public DbSet<NotificacionPago> NotificacionesPago => Set<NotificacionPago>();
    /// <summary>Tabla OutboxMessages.</summary>
    public DbSet<MensajePendiente> MensajesPendientes => Set<MensajePendiente>();

    /// <summary>Reglas generales que se aplican a todas las tablas.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder convenciones)
    {
        // Todas las fechas se guardan con precisión de milisegundos (columnas datetime2(3)).
        convenciones.Properties<DateTime>().HavePrecision(3);
        convenciones.Properties<DateTime?>().HavePrecision(3);
    }

    /// <summary>Dice a qué tabla y columna corresponde cada clase y propiedad.</summary>
    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.Entity<Pedido>(tabla =>
        {
            tabla.ToTable("Orders");
            tabla.HasKey(p => p.Id);
            tabla.Property(p => p.Id).HasColumnName("Id");
            tabla.Property(p => p.Estado).HasColumnName("Status").HasMaxLength(30)
                .HasConversion(estado => CodigosEstadoPedido.ATexto(estado), texto => CodigosEstadoPedido.DesdeTexto(texto));
            tabla.Property(p => p.RepartidorId).HasColumnName("DriverId");
            tabla.Property(p => p.TarifaEnvio).HasColumnName("DeliveryFee").HasPrecision(18, 2);
            tabla.Property(p => p.MontoTotal).HasColumnName("TotalAmount").HasPrecision(18, 2);
            tabla.Property(p => p.Moneda).HasColumnName("Currency").HasMaxLength(3).IsFixedLength();
            tabla.Property(p => p.PagoId).HasColumnName("PaymentId").HasMaxLength(100);
            tabla.Property(p => p.FechaPago).HasColumnName("PaidAt");
        });

        modelo.Entity<GananciaRepartidor>(tabla =>
        {
            tabla.ToTable("DriverEarnings");
            tabla.HasKey(g => g.Id);
            tabla.Property(g => g.PedidoId).HasColumnName("OrderId");
            tabla.Property(g => g.RepartidorId).HasColumnName("DriverId");
            tabla.Property(g => g.Monto).HasColumnName("Amount").HasPrecision(18, 2);
            tabla.Property(g => g.PorcentajeAplicado).HasColumnName("SharePercent").HasPrecision(5, 2);
            tabla.Property(g => g.PagoOrigenId).HasColumnName("SourcePaymentId").HasMaxLength(100);
            tabla.Property(g => g.FechaCreacion).HasColumnName("CreatedAt");
        });

        modelo.Entity<NotificacionPago>(tabla =>
        {
            tabla.ToTable("PaymentNotifications");
            tabla.HasKey(n => n.Id);
            tabla.Property(n => n.Pasarela).HasColumnName("Provider").HasMaxLength(50);
            tabla.Property(n => n.EventoId).HasColumnName("EventId").HasMaxLength(100);
            tabla.Property(n => n.TipoEvento).HasColumnName("EventType").HasMaxLength(50);
            tabla.Property(n => n.PagoId).HasColumnName("PaymentId").HasMaxLength(100);
            tabla.Property(n => n.PedidoId).HasColumnName("OrderId");
            tabla.Property(n => n.Monto).HasColumnName("Amount").HasPrecision(18, 2);
            tabla.Property(n => n.Moneda).HasColumnName("Currency").HasMaxLength(3).IsFixedLength();
            tabla.Property(n => n.FechaEvento).HasColumnName("OccurredAt");
            tabla.Property(n => n.FechaRecepcion).HasColumnName("ReceivedAt");
            tabla.Property(n => n.JsonOriginal).HasColumnName("RawPayload");
            tabla.Property(n => n.Resultado).HasColumnName("Outcome").HasMaxLength(40);
            tabla.Property(n => n.DetalleResultado).HasColumnName("OutcomeDetail").HasMaxLength(400);
            tabla.Property(n => n.RequiereRevision).HasColumnName("RequiresReview");
        });

        modelo.Entity<MensajePendiente>(tabla =>
        {
            tabla.ToTable("OutboxMessages");
            tabla.HasKey(m => m.Id);
            tabla.Property(m => m.Id).ValueGeneratedNever();
            tabla.Property(m => m.Tipo).HasColumnName("Type").HasMaxLength(100);
            tabla.Property(m => m.Contenido).HasColumnName("Payload");
            tabla.Property(m => m.FechaCreacion).HasColumnName("CreatedAt");
            tabla.Property(m => m.ProximoIntento).HasColumnName("NextAttemptAt");
            tabla.Property(m => m.Intentos).HasColumnName("Attempts");
            tabla.Property(m => m.BloqueadoHasta).HasColumnName("LockedUntil");
            tabla.Property(m => m.FechaEnvio).HasColumnName("ProcessedAt");
            tabla.Property(m => m.FechaDescarte).HasColumnName("DeadLetteredAt");
            tabla.Property(m => m.UltimoError).HasColumnName("LastError").HasMaxLength(2000);
        });
    }
}
