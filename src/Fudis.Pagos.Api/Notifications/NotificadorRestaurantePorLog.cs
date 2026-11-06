namespace Fudis.Pagos.Api.Notifications;

/// <summary>
/// Versión de ejemplo: en vez de avisar de verdad al restaurante, lo escribe en el log.
/// En producción se reemplaza por el cliente real (HTTP, cola...), enviando MensajeId como
/// "Idempotency-Key" para que el restaurante ignore los avisos repetidos.
/// </summary>
public sealed class NotificadorRestaurantePorLog : INotificadorRestaurante
{
    private readonly ILogger<NotificadorRestaurantePorLog> _logger;

    public NotificadorRestaurantePorLog(ILogger<NotificadorRestaurantePorLog> logger) => _logger = logger;

    /// <summary>Escribe el aviso en el log en vez de enviarlo de verdad.</summary>
    public Task AvisarPedidoPagadoAsync(AvisoPedidoPagado aviso, CancellationToken cancelacion)
    {
        _logger.LogInformation(
            "Restaurante notificado: pedido {PedidoId} pagado (pago {PagoId}, id del aviso {MensajeId}).",
            aviso.PedidoId, aviso.PagoId, aviso.MensajeId);

        return Task.CompletedTask;
    }
}
