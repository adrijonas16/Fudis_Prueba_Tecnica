using Fudis.Pagos.Api.Models.Contracts;

namespace Fudis.Pagos.UnitTests;

/// <summary>
/// Pruebas de la lectura y validación del JSON que manda la pasarela.
/// </summary>
public class SolicitudConfirmarPagoTests
{
    private const string JsonValido = """
        {"eventId":"evt_1","eventType":"payment.succeeded","paymentId":"pay_1","orderId":10,
         "amount":45000.50,"currency":"COP","occurredAt":"2026-11-05T19:00:00-05:00"}
        """;

    [Fact]
    public void Un_json_correcto_no_tiene_errores_y_la_fecha_queda_en_utc()
    {
        var solicitud = SolicitudConfirmarPago.DesdeJson(JsonValido)!;
        Assert.Empty(solicitud.Validar());

        var pago = solicitud.ConvertirAPagoRecibido(JsonValido);
        Assert.True(pago.EsPagoExitoso);
        Assert.Equal(new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), pago.FechaEventoUtc);
        Assert.Equal(45000.50m, pago.Monto);
    }

    [Fact]
    public void Un_json_mal_escrito_devuelve_null()
    {
        Assert.Null(SolicitudConfirmarPago.DesdeJson("{esto no es json"));
    }

    [Fact]
    public void Solo_con_orderId_como_el_codigo_original_falta_todo_lo_demas()
    {
        var errores = SolicitudConfirmarPago.DesdeJson("""{"orderId":10}""")!.Validar();
        Assert.Equal(["amount", "currency", "eventId", "eventType", "occurredAt", "paymentId"], errores.Keys.Order());
    }

    [Theory]
    [InlineData("\"amount\":45000.50", "\"amount\":0", "amount")]
    [InlineData("\"amount\":45000.50", "\"amount\":-5", "amount")]
    [InlineData("\"amount\":45000.50", "\"amount\":1.001", "amount")]
    [InlineData("\"currency\":\"COP\"", "\"currency\":\"cop\"", "currency")]
    [InlineData("\"currency\":\"COP\"", "\"currency\":\"PESOS\"", "currency")]
    [InlineData("\"orderId\":10", "\"orderId\":0", "orderId")]
    [InlineData("\"eventId\":\"evt_1\"", "\"eventId\":\"   \"", "eventId")]
    public void Detecta_el_campo_con_error(string original, string reemplazo, string campoConError)
    {
        var errores = SolicitudConfirmarPago.DesdeJson(JsonValido.Replace(original, reemplazo))!.Validar();
        Assert.Equal(new[] { campoConError }, errores.Keys);
    }

    [Fact]
    public void Un_pago_fallido_es_valido_pero_no_es_exitoso()
    {
        var json = JsonValido.Replace("payment.succeeded", "payment.failed");
        var solicitud = SolicitudConfirmarPago.DesdeJson(json)!;
        Assert.Empty(solicitud.Validar());
        Assert.False(solicitud.ConvertirAPagoRecibido(json).EsPagoExitoso);
    }
}
