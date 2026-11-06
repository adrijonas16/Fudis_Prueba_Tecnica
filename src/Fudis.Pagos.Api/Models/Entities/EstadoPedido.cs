namespace Fudis.Pagos.Api.Models.Entities;

/// <summary>
/// Estados posibles de un pedido.
/// En la base de datos se guardan como texto en inglés ("PAID", "PENDING_PAYMENT"...) porque así
/// los usa el sistema que ya existe. La traducción está en <see cref="CodigosEstadoPedido"/>.
/// </summary>
public enum EstadoPedido
{
    PendienteDePago,
    Pagado,
    EnReparto,
    Entregado,
    Cancelado,
    Reembolsado
}

/// <summary>Traduce el estado entre C# (español) y la base de datos (texto en inglés).</summary>
public static class CodigosEstadoPedido
{
    private static readonly Dictionary<EstadoPedido, string> EstadoATexto = new()
    {
        [EstadoPedido.PendienteDePago] = "PENDING_PAYMENT",
        [EstadoPedido.Pagado] = "PAID",
        [EstadoPedido.EnReparto] = "IN_DELIVERY",
        [EstadoPedido.Entregado] = "DELIVERED",
        [EstadoPedido.Cancelado] = "CANCELLED",
        [EstadoPedido.Reembolsado] = "REFUNDED",
    };

    /// <summary>De estado en C# a texto de base de datos. Ej: Pagado -> "PAID".</summary>
    public static string ATexto(EstadoPedido estado) => EstadoATexto[estado];

    /// <summary>De texto de base de datos a estado en C#. Ej: "PAID" -> Pagado.</summary>
    public static EstadoPedido DesdeTexto(string texto)
    {
        foreach (var (estado, textoGuardado) in EstadoATexto)
        {
            if (textoGuardado == texto)
                return estado;
        }

        // Si llega un estado que no conocemos, preferimos fallar a adivinar.
        throw new InvalidOperationException($"Estado de pedido desconocido: '{texto}'.");
    }
}
