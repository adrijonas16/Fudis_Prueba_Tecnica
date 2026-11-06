namespace Fudis.Pagos.Api.Notifications;

/// <summary>Lo que recibe el restaurante cuando un pedido se paga.</summary>
/// <param name="MensajeId">Id único del aviso. Si el restaurante lo recibe dos veces, con esto sabe que es el mismo.</param>
public sealed record AvisoPedidoPagado(Guid MensajeId, int PedidoId, string PagoId, DateTime FechaPago);

/// <summary>
/// Cómo se le avisa al restaurante. Es una interfaz para poder cambiar la forma de enviar
/// (HTTP, cola de mensajes, notificación push...) sin tocar el resto del código, y para que las
/// pruebas usen un notificador falso que falla a propósito.
/// </summary>
public interface INotificadorRestaurante
{
    Task AvisarPedidoPagadoAsync(AvisoPedidoPagado aviso, CancellationToken cancelacion);
}
