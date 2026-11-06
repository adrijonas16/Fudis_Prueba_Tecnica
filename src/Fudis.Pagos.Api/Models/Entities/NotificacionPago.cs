namespace Fudis.Pagos.Api.Models.Entities;

/// <summary>
/// Registro de cada aviso que manda la pasarela. Tabla: PaymentNotifications.
///
/// Sirve para tres cosas:
///  1. Detectar avisos repetidos: no puede haber dos con la misma (Pasarela, EventoId).
///  2. Auditoría: queda guardado qué llegó y qué hicimos.
///  3. Revisión: los casos raros quedan marcados con RequiereRevision = true.
/// </summary>
public class NotificacionPago
{
    public long Id { get; set; }
    public string Pasarela { get; set; } = "";
    public string EventoId { get; set; } = "";
    public string TipoEvento { get; set; } = "";
    public string PagoId { get; set; } = "";
    public int PedidoId { get; set; }
    public decimal Monto { get; set; }
    public string Moneda { get; set; } = "";
    public DateTime FechaEvento { get; set; }
    public DateTime FechaRecepcion { get; set; }

    /// <summary>El JSON tal cual llegó.</summary>
    public string JsonOriginal { get; set; } = "";

    /// <summary>Cómo terminó: "PROCESADO", "DUPLICADO", "MONTO_NO_COINCIDE"...</summary>
    public string Resultado { get; set; } = "RECIBIDO";

    public string? DetalleResultado { get; set; }
    public bool RequiereRevision { get; set; }
}
