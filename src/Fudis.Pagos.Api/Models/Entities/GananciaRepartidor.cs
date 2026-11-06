namespace Fudis.Pagos.Api.Models.Entities;

/// <summary>
/// Lo que gana un repartidor por un pedido. Tabla: DriverEarnings.
/// Regla clave: como máximo UNA ganancia por pedido (la base de datos lo garantiza con un índice único).
/// </summary>
public class GananciaRepartidor
{
    public long Id { get; set; }
    public int PedidoId { get; set; }
    public int RepartidorId { get; set; }
    public decimal Monto { get; set; }

    /// <summary>Porcentaje que se usó para calcular (ej. 70). Null en registros antiguos.</summary>
    public decimal? PorcentajeAplicado { get; set; }

    /// <summary>De qué pago salió esta ganancia (para poder rastrearla).</summary>
    public string? PagoOrigenId { get; set; }

    public DateTime FechaCreacion { get; set; }
}
