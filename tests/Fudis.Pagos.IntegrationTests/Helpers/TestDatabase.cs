using Fudis.Pagos.Api.Database;
using Fudis.Pagos.Api.Configuration;
using Fudis.Pagos.Api.Models.Contracts;
using Fudis.Pagos.Api.Models.Entities;
using Fudis.Pagos.Api.Notifications;
using Fudis.Pagos.Api.Repositories;
using Fudis.Pagos.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Fudis.Pagos.IntegrationTests.Helpers;

/// <summary>Atajos para las pruebas: crear pedidos, confirmar pagos y ver cómo quedó la base de datos.</summary>
public sealed class TestDatabase
{
    public TestDatabase(string cadenaConexion)
    {
        CadenaConexion = cadenaConexion;
        Reloj = new FakeTimeProvider(DateTimeOffset.UtcNow); // reloj que las pruebas pueden adelantar
    }

    public string CadenaConexion { get; }
    public FakeTimeProvider Reloj { get; }

    public FudisDbContext CrearContexto() => new(new DbContextOptionsBuilder<FudisDbContext>()
        .UseSqlServer(CadenaConexion, sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null))
        .Options);

    /// <summary>Confirma un pago como lo haría una petición HTTP: con su propio contexto y repositorios.</summary>
    public async Task<ResultadoConfirmacion> ConfirmarAsync(PagoRecibido pago)
    {
        await using var db = CrearContexto();
        var servicio = new ConfirmacionPagoService(
            new UnitOfWork(db),
            new CalculoGananciaService(),
            new PagosOptions(),
            Reloj,
            NullLogger<ConfirmacionPagoService>.Instance);

        return await servicio.ConfirmarPagoAsync(pago, CancellationToken.None);
    }

    /// <summary>Ejecuta una vuelta del envío de notificaciones. Devuelve cuántos mensajes tomó.</summary>
    public async Task<int> EnviarNotificacionesAsync(INotificadorRestaurante notificador, EnvioNotificacionesOptions opciones)
    {
        await using var db = CrearContexto();
        var servicio = new EnvioNotificacionesService(
            new UnitOfWork(db), notificador, opciones, Reloj, NullLogger<EnvioNotificacionesService>.Instance);
        return await servicio.EnviarPendientesAsync(CancellationToken.None);
    }

    public async Task<int> CrearPedidoAsync(
        EstadoPedido estado = EstadoPedido.PendienteDePago,
        int? repartidorId = 7,
        decimal tarifaEnvio = 5000m,
        decimal? montoTotal = 45000m,
        string? moneda = "COP")
    {
        await using var db = CrearContexto();
        var pedido = new Pedido { Estado = estado, RepartidorId = repartidorId, TarifaEnvio = tarifaEnvio, MontoTotal = montoTotal, Moneda = moneda };
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido.Id;
    }

    /// <summary>Crea un aviso de pago de prueba. Por defecto: pago exitoso de 45.000 COP.</summary>
    public static PagoRecibido Pago(
        int pedidoId,
        string? eventoId = null,
        string pagoId = "pay_1",
        decimal monto = 45000m,
        string moneda = "COP",
        string tipoEvento = SolicitudConfirmarPago.PagoExitoso) =>
        new(eventoId ?? "evt_" + Guid.NewGuid().ToString("N"), tipoEvento, pagoId, pedidoId, monto, moneda, DateTime.UtcNow, "{}");

    /// <summary>Foto de cómo quedó todo lo relacionado con un pedido.</summary>
    public async Task<Estado> VerEstadoAsync(int pedidoId)
    {
        await using var db = CrearContexto();
        var pedido = await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId);
        var ganancias = await db.GananciasRepartidor.AsNoTracking().Where(g => g.PedidoId == pedidoId).ToListAsync();
        var mensajes = await db.MensajesPendientes.CountAsync();
        var notificaciones = await db.NotificacionesPago.AsNoTracking().Where(n => n.PedidoId == pedidoId).ToListAsync();
        return new Estado(pedido, ganancias, mensajes, notificaciones.Count, notificaciones.Count(n => n.RequiereRevision));
    }

    public sealed record Estado(Pedido Pedido, List<GananciaRepartidor> Ganancias, int MensajesPendientes, int Notificaciones, int MarcadasParaRevision);
}
