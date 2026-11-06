namespace Fudis.Pagos.Api.Models.Entities;

/// <summary>
/// Un aviso que hay que enviar (por ejemplo, "avisarle al restaurante que su pedido se pagó"). Tabla: OutboxMessages.
///
/// ¿Por qué se guarda en una tabla en vez de enviarse de inmediato?
/// Porque se guarda en la MISMA transacción que el pago. Así es imposible que el pedido quede pagado
/// y el aviso se pierda. Después, el EnvioNotificacionesWorker lo lee y lo envía (reintentando si falla).
/// A esto se le llama "patrón Outbox".
/// </summary>
public class MensajePendiente
{
    /// <summary>Id único. El restaurante lo recibe para detectar si le llega el mismo aviso dos veces.</summary>
    public Guid Id { get; set; }

    /// <summary>Qué tipo de aviso es (ej. "restaurante.pedido-pagado.v1").</summary>
    public string Tipo { get; set; } = "";

    /// <summary>El contenido del aviso, en JSON.</summary>
    public string Contenido { get; set; } = "";

    public DateTime FechaCreacion { get; set; }

    /// <summary>A partir de cuándo se puede (re)intentar enviar.</summary>
    public DateTime ProximoIntento { get; set; }

    public int Intentos { get; set; }

    /// <summary>Mientras esté en el futuro, un trabajador lo está enviando y los demás no lo tocan.</summary>
    public DateTime? BloqueadoHasta { get; set; }

    /// <summary>Cuándo se envió con éxito. Null = todavía pendiente.</summary>
    public DateTime? FechaEnvio { get; set; }

    /// <summary>Cuándo nos rendimos tras demasiados intentos fallidos. Hay que revisarlo a mano.</summary>
    public DateTime? FechaDescarte { get; set; }

    public string? UltimoError { get; set; }
}

/// <summary>Tipos de mensaje que existen. El ".v1" permite cambiar el formato en el futuro sin romper los mensajes viejos.</summary>
public static class TiposDeMensaje
{
    public const string AvisoPedidoPagado = "restaurante.pedido-pagado.v1";
}
