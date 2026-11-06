using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Fudis.Pagos.Api.Models.Contracts;

/// <summary>
/// El JSON que manda la pasarela (lo que ENTRA al endpoint).
/// Los nombres del JSON van en inglés ("eventId", "orderId"...) porque es el contrato con la pasarela.
/// Ejemplo:
/// {"eventId":"evt_1","eventType":"payment.succeeded","paymentId":"pay_1","orderId":10,
///  "amount":45000.00,"currency":"COP","occurredAt":"2026-11-05T19:00:00Z"}
/// </summary>
public sealed class SolicitudConfirmarPago
{
    public const string PagoExitoso = "payment.succeeded";
    public const string PagoFallido = "payment.failed";

    [JsonPropertyName("eventId")] public string? EventoId { get; init; }
    [JsonPropertyName("eventType")] public string? TipoEvento { get; init; }
    [JsonPropertyName("paymentId")] public string? PagoId { get; init; }
    [JsonPropertyName("orderId")] public int? PedidoId { get; init; }
    [JsonPropertyName("amount")] public decimal? Monto { get; init; }
    [JsonPropertyName("currency")] public string? Moneda { get; init; }
    [JsonPropertyName("occurredAt")] public DateTimeOffset? FechaEvento { get; init; }

    private static readonly Regex FormatoMoneda = new("^[A-Z]{3}$");

    /// <summary>Convierte el texto JSON en este objeto. Devuelve null si el JSON está mal escrito.</summary>
    public static SolicitudConfirmarPago? DesdeJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SolicitudConfirmarPago>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Revisa que no falte nada y que los valores tengan sentido.
    /// Devuelve la lista de errores por campo. Vacía = todo bien.
    /// </summary>
    public Dictionary<string, string[]> Validar()
    {
        var errores = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(EventoId) || EventoId.Length > 100)
            errores["eventId"] = ["Obligatorio, máximo 100 caracteres."];

        if (string.IsNullOrWhiteSpace(TipoEvento) || TipoEvento.Length > 50)
            errores["eventType"] = ["Obligatorio, máximo 50 caracteres."];

        if (string.IsNullOrWhiteSpace(PagoId) || PagoId.Length > 100)
            errores["paymentId"] = ["Obligatorio, máximo 100 caracteres."];

        if (PedidoId is null || PedidoId <= 0)
            errores["orderId"] = ["Obligatorio y mayor que 0."];

        if (Monto is null || Monto <= 0 || decimal.Round(Monto.Value, 2) != Monto.Value)
            errores["amount"] = ["Obligatorio, mayor que 0 y con máximo 2 decimales."];

        if (Moneda is null || !FormatoMoneda.IsMatch(Moneda))
            errores["currency"] = ["Obligatorio, código de 3 letras en mayúsculas (ej. COP)."];

        if (FechaEvento is null)
            errores["occurredAt"] = ["Obligatorio, fecha con zona horaria (ej. 2026-11-05T19:00:00Z)."];

        return errores;
    }

    /// <summary>
    /// Pasa los datos a un objeto sin campos vacíos, listo para el servicio.
    /// Solo se llama DESPUÉS de Validar() sin errores.
    /// </summary>
    public PagoRecibido ConvertirAPagoRecibido(string jsonOriginal) => new(
        EventoId: EventoId!,
        TipoEvento: TipoEvento!,
        PagoId: PagoId!,
        PedidoId: PedidoId!.Value,
        Monto: Monto!.Value,
        Moneda: Moneda!,
        FechaEventoUtc: FechaEvento!.Value.UtcDateTime,
        JsonOriginal: jsonOriginal);
}

/// <summary>Los datos del pago ya validados (ningún campo es null). Es lo que recibe el servicio.</summary>
public sealed record PagoRecibido(
    string EventoId,
    string TipoEvento,
    string PagoId,
    int PedidoId,
    decimal Monto,
    string Moneda,
    DateTime FechaEventoUtc,
    string JsonOriginal)
{
    /// <summary>true si el aviso dice que el pago se hizo (payment.succeeded).</summary>
    public bool EsPagoExitoso => TipoEvento == SolicitudConfirmarPago.PagoExitoso;
}
