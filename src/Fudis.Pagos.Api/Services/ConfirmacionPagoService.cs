using System.Text.Json;
using Fudis.Pagos.Api.Configuration;
using Fudis.Pagos.Api.Models.Contracts;
using Fudis.Pagos.Api.Models.Entities;
using Fudis.Pagos.Api.Notifications;
using Fudis.Pagos.Api.Repositories;

namespace Fudis.Pagos.Api.Services;

/// <summary>
/// LÓGICA PRINCIPAL: qué hacer cuando la pasarela avisa que llegó un pago.
///
/// Todo ocurre en UNA transacción (o se guarda todo, o no se guarda nada):
///   PASO 1. Registrar el aviso. Si ya había llegado antes -> es repetido, no se hace nada más.
///   PASO 2. Marcar el pedido como pagado (solo si estaba pendiente y el monto coincide).
///   PASO 3. Registrar la ganancia del repartidor (70 % del envío).
///   PASO 4. Dejar pendiente el aviso al restaurante (lo envía después el EnvioNotificacionesWorker).
///   PASO 5. Guardar cómo terminó el aviso y confirmar la transacción.
/// </summary>
public sealed class ConfirmacionPagoService
{
    private readonly UnitOfWork _unitOfWork;                       // acceso a TODAS las tablas
    private readonly CalculoGananciaService _calculoGananciaService; // la regla del 70 %
    private readonly PagosOptions _opciones;                        // configuración (appsettings.json)
    private readonly TimeProvider _reloj;                           // la hora actual
    private readonly ILogger<ConfirmacionPagoService> _logger;      // el log

    public ConfirmacionPagoService(
        UnitOfWork unitOfWork,
        CalculoGananciaService calculoGananciaService,
        PagosOptions opciones,
        TimeProvider reloj,
        ILogger<ConfirmacionPagoService> logger)
    {
        _unitOfWork = unitOfWork;
        _calculoGananciaService = calculoGananciaService;
        _opciones = opciones;
        _reloj = reloj;
        _logger = logger;
    }

    /// <summary>Punto de entrada. Lo llama el controlador.</summary>
    public async Task<ResultadoConfirmacion> ConfirmarPagoAsync(PagoRecibido pago, CancellationToken cancelacion)
    {
        // Si la base de datos falla un instante, se repite todo desde el principio.
        // Es seguro repetir: si la primera vez sí se guardó, el PASO 1 lo detecta como repetido.
        var resultado = await _unitOfWork.EjecutarConReintentosAsync(
            () => ConfirmarEnUnaTransaccionAsync(pago, cancelacion));

        EscribirEnLog(pago, resultado);
        return resultado;
    }

    /// <summary>Los 5 pasos dentro de una sola transacción. Si algo falla, nada queda guardado.</summary>
    private async Task<ResultadoConfirmacion> ConfirmarEnUnaTransaccionAsync(PagoRecibido pago, CancellationToken cancelacion)
    {
        _unitOfWork.LimpiarMemoria(); // por si es un reintento
        var ahora = _reloj.AhoraUtc();

        await using var transaccion = await _unitOfWork.IniciarTransaccionAsync(cancelacion);

        // ---- PASO 1: registrar el aviso (y detectar si es repetido) ----
        var notificacion = CrearRegistroDeNotificacion(pago, ahora);
        bool esPrimeraVez = await _unitOfWork.NotificacionesPago.IntentarRegistrarAsync(notificacion, cancelacion);

        if (!esPrimeraVez)
        {
            await transaccion.RollbackAsync(cancelacion);
            _unitOfWork.LimpiarMemoria();

            var resultadoOriginal = await _unitOfWork.NotificacionesPago.BuscarResultadoAsync(_opciones.NombrePasarela, pago.EventoId, cancelacion);
            return new ResultadoConfirmacion(TipoResultado.Duplicado, $"Este aviso ya había llegado (resultado original: {resultadoOriginal}).");
        }

        // ---- PASOS 2, 3 y 4: procesar el pago ----
        var resultado = await ProcesarPagoAsync(pago, ahora, cancelacion);

        // ---- PASO 5: guardar cómo terminó y confirmar todo ----
        notificacion.Resultado = resultado.Codigo;
        notificacion.DetalleResultado = Recortar(resultado.Detalle, 400);
        notificacion.RequiereRevision = resultado.RequiereRevision;

        await _unitOfWork.GuardarCambiosAsync(cancelacion);
        await transaccion.CommitAsync(cancelacion); // <- aquí todo se vuelve definitivo
        return resultado;
    }

    /// <summary>Pasos 2, 3 y 4: marcar pagado, registrar ganancia y dejar el aviso al restaurante pendiente.</summary>
    private async Task<ResultadoConfirmacion> ProcesarPagoAsync(PagoRecibido pago, DateTime ahora, CancellationToken cancelacion)
    {
        // Si el aviso no dice "pago exitoso" (ej. "payment.failed"), solo se deja anotado.
        if (!pago.EsPagoExitoso)
            return new ResultadoConfirmacion(TipoResultado.Ignorado, $"El tipo de aviso '{pago.TipoEvento}' no confirma un pago.");

        // ---- PASO 2: marcar el pedido como pagado ----
        bool seMarcoComoPagado = await _unitOfWork.Pedidos.IntentarMarcarComoPagadoAsync(
            pago.PedidoId, pago.PagoId, pago.Monto, pago.Moneda, ahora, cancelacion);

        if (!seMarcoComoPagado)
            return await AveriguarPorQueNoSePudoPagarAsync(pago, cancelacion);

        var pedido = await _unitOfWork.Pedidos.BuscarPorIdAsync(pago.PedidoId, cancelacion)
                     ?? throw new InvalidOperationException("El pedido acaba de marcarse como pagado y no se encuentra.");

        // ---- PASO 3: registrar la ganancia del repartidor ----
        bool seRegistroGanancia = RegistrarGananciaDelRepartidor(pedido, pago.PagoId, ahora);

        // ---- PASO 4: dejar pendiente el aviso al restaurante ----
        DejarPendienteAvisoAlRestaurante(pedido.Id, pago.PagoId, ahora);

        string detalle = seRegistroGanancia
            ? "Pedido pagado; ganancia registrada; aviso al restaurante pendiente de envío."
            : "Pedido pagado sin repartidor asignado: la ganancia se registrará al asignarlo; aviso al restaurante pendiente de envío.";

        return new ResultadoConfirmacion(TipoResultado.Procesado, detalle);
    }

    /// <returns>true si se registró la ganancia; false si el pedido aún no tiene repartidor.</returns>
    private bool RegistrarGananciaDelRepartidor(Pedido pedido, string pagoId, DateTime ahora)
    {
        // Sin repartidor no hay a quién pagarle. No inventamos uno ni hacemos fallar el pago:
        // la ganancia la debe crear el proceso de asignación de repartidor (pendiente, ver README).
        if (pedido.RepartidorId is null)
            return false;

        _unitOfWork.Ganancias.Agregar(new GananciaRepartidor
        {
            PedidoId = pedido.Id,
            RepartidorId = pedido.RepartidorId.Value,
            Monto = _calculoGananciaService.Calcular(pedido.TarifaEnvio),
            PorcentajeAplicado = _calculoGananciaService.Porcentaje,
            PagoOrigenId = pagoId,
            FechaCreacion = ahora
        });

        return true;
    }

    /// <summary>
    /// No se le avisa al restaurante ahora mismo. Se guarda un "mensaje pendiente" en la MISMA transacción.
    ///
    /// ¿Por qué? Si se avisara ahora y luego la transacción fallara, el restaurante cocinaría un pedido que
    /// no quedó pagado. Y si se guardara el pago y luego el aviso fallara, el restaurante nunca se enteraría
    /// (ese era un error del código original). Guardándolo junto con el pago, ninguna de las dos cosas pasa.
    /// </summary>
    private void DejarPendienteAvisoAlRestaurante(int pedidoId, string pagoId, DateTime ahora)
    {
        var mensajeId = Guid.NewGuid();
        var aviso = new AvisoPedidoPagado(mensajeId, pedidoId, pagoId, ahora);

        _unitOfWork.MensajesPendientes.Agregar(new MensajePendiente
        {
            Id = mensajeId,
            Tipo = TiposDeMensaje.AvisoPedidoPagado,
            Contenido = JsonSerializer.Serialize(aviso),
            FechaCreacion = ahora,
            ProximoIntento = ahora // se puede enviar ya
        });
    }

    /// <summary>El pedido no se marcó como pagado. Se revisa por qué, en este orden.</summary>
    private async Task<ResultadoConfirmacion> AveriguarPorQueNoSePudoPagarAsync(PagoRecibido pago, CancellationToken cancelacion)
    {
        var pedido = await _unitOfWork.Pedidos.BuscarPorIdAsync(pago.PedidoId, cancelacion);

        if (pedido is null)
            return new ResultadoConfirmacion(TipoResultado.PedidoNoExiste, "El pedido no existe.");

        // Ya estaba pagado con ESTE mismo pago: es un reintento normal de la pasarela. Todo bien.
        if (pedido.PagoId == pago.PagoId)
            return new ResultadoConfirmacion(TipoResultado.YaPagado, "El pedido ya fue pagado con este mismo pago.");

        // Ya estaba pagado con OTRO pago: al cliente le cobraron dos veces.
        if (pedido.PagoId is not null)
            return new ResultadoConfirmacion(TipoResultado.PagoEnConflicto,
                $"El pedido ya fue pagado con otro pago ({pedido.PagoId}). Posible cobro doble: revisar y reembolsar.");

        // Está en un estado que no se puede pagar (ej. cancelado).
        if (!Pedido.SePuedePagar(pedido.Estado))
            return new ResultadoConfirmacion(TipoResultado.PedidoNoPagable,
                $"El pedido está en estado {CodigosEstadoPedido.ATexto(pedido.Estado)}; no se puede marcar como pagado.");

        // Lo único que queda: el monto o la moneda no coinciden.
        string montoEsperado = pedido.MontoTotal?.ToString("0.00") ?? "(sin monto)";
        string monedaEsperada = pedido.Moneda ?? "(sin moneda)";
        return new ResultadoConfirmacion(TipoResultado.MontoNoCoincide,
            $"Se esperaba {montoEsperado} {monedaEsperada} y llegó {pago.Monto:0.00} {pago.Moneda}.");
    }

    /// <summary>Arma el registro del aviso tal como llegó, para guardarlo en PaymentNotifications.</summary>
    private NotificacionPago CrearRegistroDeNotificacion(PagoRecibido pago, DateTime ahora) => new()
    {
        Pasarela = _opciones.NombrePasarela,
        EventoId = pago.EventoId,
        TipoEvento = pago.TipoEvento,
        PagoId = pago.PagoId,
        PedidoId = pago.PedidoId,
        Monto = pago.Monto,
        Moneda = pago.Moneda,
        FechaEvento = pago.FechaEventoUtc,
        FechaRecepcion = ahora,
        JsonOriginal = pago.JsonOriginal
    };

    /// <summary>Escribe el resultado en el log. Los casos que requieren revisión van como advertencia (Warning).</summary>
    private void EscribirEnLog(PagoRecibido pago, ResultadoConfirmacion resultado)
    {
        if (resultado.RequiereRevision)
        {
            _logger.LogWarning("Aviso de pago {EventoId} (pedido {PedidoId}) REQUIERE REVISIÓN: {Resultado}. {Detalle}",
                pago.EventoId, pago.PedidoId, resultado.Codigo, resultado.Detalle);
        }
        else
        {
            _logger.LogInformation("Aviso de pago {EventoId} (pedido {PedidoId}): {Resultado}. {Detalle}",
                pago.EventoId, pago.PedidoId, resultado.Codigo, resultado.Detalle);
        }
    }

    /// <summary>Corta un texto si supera el largo máximo de la columna.</summary>
    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
