namespace Fudis.Pagos.Api.Models.Contracts;

/// <summary>Todas las formas en que puede terminar un aviso de pago.</summary>
public enum TipoResultado
{
    /// <summary>Todo bien: pedido pagado, ganancia registrada, aviso al restaurante pendiente de envío.</summary>
    Procesado,

    /// <summary>Este mismo aviso (mismo EventoId) ya había llegado. No se hace nada.</summary>
    Duplicado,

    /// <summary>Otro aviso del mismo pago ya lo había procesado. No se hace nada.</summary>
    YaPagado,

    /// <summary>El aviso no confirma un pago (ej. "payment.failed"). Se anota y se ignora.</summary>
    Ignorado,

    // ---- Los siguientes son casos raros: una persona debe revisarlos ----

    /// <summary>El pedido no existe.</summary>
    PedidoNoExiste,

    /// <summary>El monto o la moneda no coinciden con el pedido.</summary>
    MontoNoCoincide,

    /// <summary>El pedido no se puede pagar (ej. está cancelado). Posible reembolso.</summary>
    PedidoNoPagable,

    /// <summary>El pedido ya estaba pagado con OTRO pago. ¡Posible cobro doble al cliente!</summary>
    PagoEnConflicto
}

/// <summary>Lo que devuelve el servicio: qué pasó y una explicación.</summary>
public sealed record ResultadoConfirmacion(TipoResultado Tipo, string Detalle)
{
    /// <summary>Texto que se guarda en la base de datos y se devuelve a la pasarela.</summary>
    public string Codigo => Tipo switch
    {
        TipoResultado.Procesado => "PROCESADO",
        TipoResultado.Duplicado => "DUPLICADO",
        TipoResultado.YaPagado => "YA_PAGADO",
        TipoResultado.Ignorado => "IGNORADO",
        TipoResultado.PedidoNoExiste => "PEDIDO_NO_EXISTE",
        TipoResultado.MontoNoCoincide => "MONTO_NO_COINCIDE",
        TipoResultado.PedidoNoPagable => "PEDIDO_NO_PAGABLE",
        TipoResultado.PagoEnConflicto => "PAGO_EN_CONFLICTO",
        _ => throw new ArgumentOutOfRangeException(nameof(Tipo))
    };

    /// <summary>true si una persona debe revisar este caso (posible problema de dinero).</summary>
    public bool RequiereRevision => Tipo is
        TipoResultado.PedidoNoExiste or
        TipoResultado.MontoNoCoincide or
        TipoResultado.PedidoNoPagable or
        TipoResultado.PagoEnConflicto;
}

/// <summary>Lo que el endpoint le responde a la pasarela (JSON de salida).</summary>
public sealed record RespuestaConfirmarPago(string Resultado, bool RequiereRevision, string Detalle);
