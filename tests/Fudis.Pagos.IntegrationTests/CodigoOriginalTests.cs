using Fudis.Pagos.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Fudis.Pagos.IntegrationTests;

/// <summary>
/// Demuestran los errores del código ORIGINAL que nos dieron, sobre el esquema original (solo script 000).
/// Son la evidencia de los hallazgos del README. Contrastan con las pruebas de la versión nueva.
/// </summary>
[Collection(SqlServerCollection.Nombre)]
public class CodigoOriginalTests
{
    private readonly SqlServerFixture _servidor;
    public CodigoOriginalTests(SqlServerFixture servidor) => _servidor = servidor;

    private Task<string> BaseConEsquemaOriginalAsync() =>
        _servidor.CrearBaseDeDatosAsync(script => script.StartsWith("000", StringComparison.Ordinal));

    [Fact]
    public async Task Codigo_original_dos_avisos_simultaneos_le_pagan_dos_veces_al_repartidor()
    {
        var cadena = await BaseConEsquemaOriginalAsync();
        var pedidoId = await CrearPedidoAsync(cadena);

        // Simulamos dos peticiones que llegan a la vez: las dos leen el pedido ANTES de que alguna guarde.
        await using var peticionA = new BaseOriginal(cadena);
        await using var peticionB = new BaseOriginal(cadena);
        var pedidoLeidoPorA = await peticionA.Orders.FindAsync(pedidoId);
        var pedidoLeidoPorB = await peticionB.Orders.FindAsync(pedidoId);
        Assert.NotEqual("PAID", pedidoLeidoPorA!.Status); // las dos pasan el "if (order.Status == "PAID")"
        Assert.NotEqual("PAID", pedidoLeidoPorB!.Status);

        foreach (var (db, pedido) in new[] { (peticionA, pedidoLeidoPorA), (peticionB, pedidoLeidoPorB) })
        {
            pedido.Status = "PAID";
            db.DriverEarnings.Add(new GananciaOriginal { OrderId = pedido.Id, DriverId = pedido.DriverId, Amount = pedido.DeliveryFee * 0.70m });
            await db.SaveChangesAsync();
        }

        await using var revisar = new BaseOriginal(cadena);
        Assert.Equal(2, await revisar.DriverEarnings.CountAsync(g => g.OrderId == pedidoId)); // ERROR: 2 ganancias
    }

    [Fact]
    public async Task Codigo_original_si_falla_el_aviso_al_restaurante_nunca_se_reintenta()
    {
        var cadena = await BaseConEsquemaOriginalAsync();
        var pedidoId = await CrearPedidoAsync(cadena);
        var notificador = new NotificadorQueFalla(fallosAntesDeFuncionar: 1);

        // 1er intento: guarda el pago y luego falla el aviso -> error 500 -> la pasarela reintenta.
        await Assert.ThrowsAsync<HttpRequestException>(() => ConfirmPaymentOriginal(cadena, pedidoId, notificador));
        // 2º intento: ve el pedido PAID y responde Ok sin avisar.
        var segundo = await ConfirmPaymentOriginal(cadena, pedidoId, notificador);

        Assert.Equal("Ok", segundo);
        Assert.Equal(0, notificador.AvisosEntregados); // ERROR: el restaurante nunca se entera
    }

    [Fact]
    public async Task Codigo_original_marca_pagado_un_pedido_cancelado_solo_con_el_id()
    {
        var cadena = await BaseConEsquemaOriginalAsync();
        var pedidoId = await CrearPedidoAsync(cadena, estado: "CANCELLED");

        // Cualquiera que conozca un número de pedido puede llamar al endpoint: no hay firma, ni monto.
        var resultado = await ConfirmPaymentOriginal(cadena, pedidoId, new NotificadorQueFalla(0));

        Assert.Equal("Ok", resultado);
        await using var revisar = new BaseOriginal(cadena);
        Assert.Equal("PAID", (await revisar.Orders.FindAsync(pedidoId))!.Status); // ERROR: un cancelado "revive"
    }

    /// <summary>Copia fiel del endpoint original (solo cambia cómo recibe sus dependencias).</summary>
    private static async Task<string> ConfirmPaymentOriginal(string cadena, int orderId, NotificadorQueFalla _notificationService)
    {
        await using var _db = new BaseOriginal(cadena);
        var order = await _db.Orders.FindAsync(orderId);
        if (order == null) return "NotFound";
        if (order.Status == "PAID") return "Ok";

        order.Status = "PAID";
        _db.DriverEarnings.Add(new GananciaOriginal { OrderId = order.Id, DriverId = order.DriverId, Amount = order.DeliveryFee * 0.70m });
        await _db.SaveChangesAsync();

        await _notificationService.NotifyRestaurant(order.Id);
        return "Ok";
    }

    private static async Task<int> CrearPedidoAsync(string cadena, string estado = "PENDING_PAYMENT")
    {
        await using var db = new BaseOriginal(cadena);
        var pedido = new PedidoOriginal { Status = estado, DriverId = 7, DeliveryFee = 5000m };
        db.Orders.Add(pedido);
        await db.SaveChangesAsync();
        return pedido.Id;
    }

    private sealed class NotificadorQueFalla(int fallosAntesDeFuncionar)
    {
        private int _llamadas;
        public int AvisosEntregados { get; private set; }

        public Task NotifyRestaurant(int orderId)
        {
            _llamadas++;
            if (_llamadas <= fallosAntesDeFuncionar)
                throw new HttpRequestException("Servicio de notificaciones caído");
            AvisosEntregados++;
            return Task.CompletedTask;
        }
    }

    // Modelo del sistema original (nombres en inglés tal como estaban).
    private sealed class PedidoOriginal
    {
        public int Id { get; set; }
        public string Status { get; set; } = "";
        public int? DriverId { get; set; }
        public decimal DeliveryFee { get; set; }
    }

    private sealed class GananciaOriginal
    {
        public long Id { get; set; }
        public int OrderId { get; set; }
        public int? DriverId { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class BaseOriginal(string cadena) : DbContext
    {
        public DbSet<PedidoOriginal> Orders => Set<PedidoOriginal>();
        public DbSet<GananciaOriginal> DriverEarnings => Set<GananciaOriginal>();

        protected override void OnConfiguring(DbContextOptionsBuilder opciones) => opciones.UseSqlServer(cadena);

        protected override void OnModelCreating(ModelBuilder modelo)
        {
            modelo.Entity<PedidoOriginal>().ToTable("Orders").Property(p => p.DeliveryFee).HasPrecision(18, 2);
            modelo.Entity<GananciaOriginal>().ToTable("DriverEarnings").Property(g => g.Amount).HasPrecision(18, 2);
        }
    }
}
