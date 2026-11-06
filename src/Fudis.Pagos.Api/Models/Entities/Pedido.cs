namespace Fudis.Pagos.Api.Models.Entities;

/// <summary>
/// Un pedido de comida. Tabla: Orders.
/// El enunciado da: Id, Status, DriverId, DeliveryFee.
/// Agregamos (ver README, supuestos): TotalAmount, Currency, PaymentId, PaidAt.
/// </summary>
public class Pedido
{
    public int Id { get; set; }

    public EstadoPedido Estado { get; set; }

    /// <summary>Repartidor asignado. Puede ser null: el pago puede llegar antes de asignar repartidor.</summary>
    public int? RepartidorId { get; set; }

    /// <summary>Lo que cuesta el envío. El repartidor gana un porcentaje de esto.</summary>
    public decimal TarifaEnvio { get; set; }

    /// <summary>Lo que el cliente debe pagar en total. Se compara con lo que dice la pasarela.</summary>
    public decimal? MontoTotal { get; set; }

    /// <summary>Moneda del pedido, código de 3 letras (ej. "COP").</summary>
    public string? Moneda { get; set; }

    /// <summary>Identificador del pago (de la pasarela) que pagó este pedido. Null si no está pagado.</summary>
    public string? PagoId { get; set; }

    public DateTime? FechaPago { get; set; }

    /// <summary>Regla de negocio: solo un pedido pendiente de pago puede pasar a pagado.</summary>
    public static bool SePuedePagar(EstadoPedido estado) => estado == EstadoPedido.PendienteDePago;
}
