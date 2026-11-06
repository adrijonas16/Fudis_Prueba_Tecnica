using System.Text.Json;
using Fudis.Pagos.Api.Configuration;
using Fudis.Pagos.Api.Models.Entities;
using Fudis.Pagos.Api.Notifications;
using Fudis.Pagos.Api.Repositories;

namespace Fudis.Pagos.Api.Services;

/// <summary>
/// Envía los mensajes pendientes (avisos al restaurante) que dejó el ConfirmacionPagoService.
///
/// Por cada mensaje:
///   - Si se envía bien  -> se marca como enviado.
///   - Si falla          -> se cuenta el intento y se programa otro más tarde (cada vez espera el doble).
///   - Si falla muchas veces -> se descarta y queda para revisión manual.
///
/// Garantía: cada mensaje se envía AL MENOS una vez. En un caso raro (el sistema se cae justo después de
/// enviar y antes de anotarlo) podría enviarse dos veces; por eso el aviso lleva un MensajeId único.
/// </summary>
public sealed class EnvioNotificacionesService
{
    private readonly UnitOfWork _unitOfWork;
    private readonly INotificadorRestaurante _notificadorRestaurante;
    private readonly EnvioNotificacionesOptions _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<EnvioNotificacionesService> _logger;

    public EnvioNotificacionesService(
        UnitOfWork unitOfWork,
        INotificadorRestaurante notificadorRestaurante,
        EnvioNotificacionesOptions opciones,
        TimeProvider reloj,
        ILogger<EnvioNotificacionesService> logger)
    {
        _unitOfWork = unitOfWork;
        _notificadorRestaurante = notificadorRestaurante;
        _opciones = opciones;
        _reloj = reloj;
        _logger = logger;
    }

    /// <summary>Envía un lote de mensajes pendientes. Devuelve cuántos tomó.</summary>
    public async Task<int> EnviarPendientesAsync(CancellationToken cancelacion)
    {
        var ahora = _reloj.AhoraUtc();
        var mensajes = await _unitOfWork.MensajesPendientes.ApartarLoteAsync(
            _opciones.TamanoLote, ahora, ahora + _opciones.TiempoDeBloqueo, cancelacion);

        foreach (var mensaje in mensajes)
        {
            await IntentarEnviarAsync(mensaje, cancelacion);
            mensaje.BloqueadoHasta = null; // se libera, haya salido bien o mal

            // Se guarda mensaje por mensaje: si el sistema se cae a mitad del lote, no se pierde lo avanzado.
            await _unitOfWork.GuardarCambiosAsync(CancellationToken.None);
        }

        return mensajes.Count;
    }

    /// <summary>Intenta enviar UN mensaje, con tiempo límite. Si sale bien lo marca enviado; si falla, programa el reintento.</summary>
    private async Task IntentarEnviarAsync(MensajePendiente mensaje, CancellationToken cancelacion)
    {
        mensaje.Intentos++;

        // Límite de tiempo para este envío: si el restaurante no responde a tiempo, se cancela y cuenta como fallo.
        using var limiteDeTiempo = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        limiteDeTiempo.CancelAfter(_opciones.TiempoMaximoPorEnvio);

        try
        {
            await EnviarSegunTipoAsync(mensaje, limiteDeTiempo.Token);

            mensaje.FechaEnvio = _reloj.AhoraUtc();
            mensaje.UltimoError = null;
        }
        catch (Exception error) when (!cancelacion.IsCancellationRequested)
        {
            RegistrarFallo(mensaje, error);
        }
    }

    /// <summary>Según el tipo de mensaje, decide a quién y cómo enviarlo.</summary>
    private Task EnviarSegunTipoAsync(MensajePendiente mensaje, CancellationToken cancelacion)
    {
        switch (mensaje.Tipo)
        {
            case TiposDeMensaje.AvisoPedidoPagado:
                var aviso = JsonSerializer.Deserialize<AvisoPedidoPagado>(mensaje.Contenido)
                            ?? throw new InvalidOperationException($"El mensaje {mensaje.Id} no tiene contenido.");
                return _notificadorRestaurante.AvisarPedidoPagadoAsync(aviso, cancelacion);

            default:
                throw new InvalidOperationException($"No se sabe enviar mensajes de tipo '{mensaje.Tipo}'.");
        }
    }

    /// <summary>Anota el error y decide: reintentar más tarde (esperando el doble) o descartar si ya fueron demasiados intentos.</summary>
    private void RegistrarFallo(MensajePendiente mensaje, Exception error)
    {
        var ahora = _reloj.AhoraUtc();
        var textoError = error is OperationCanceledException
            ? $"Tiempo agotado: el restaurante no respondió en {_opciones.TiempoMaximoPorEnvio.TotalSeconds} s."
            : $"{error.GetType().Name}: {error.Message}";
        mensaje.UltimoError = textoError.Length <= 2000 ? textoError : textoError[..2000];

        if (mensaje.Intentos >= _opciones.MaximoIntentos)
        {
            // Demasiados intentos: nos rendimos. Queda en la tabla para que alguien lo revise.
            mensaje.FechaDescarte = ahora;
            _logger.LogError(error, "Mensaje {MensajeId} descartado tras {Intentos} intentos.", mensaje.Id, mensaje.Intentos);
        }
        else
        {
            // Se reintentará más tarde. Cada fallo espera el doble que el anterior.
            mensaje.ProximoIntento = ahora + _opciones.CalcularEspera(mensaje.Intentos);
            _logger.LogWarning(error, "Falló el envío del mensaje {MensajeId} (intento {Intentos}). Próximo intento: {ProximoIntento:o}.",
                mensaje.Id, mensaje.Intentos, mensaje.ProximoIntento);
        }
    }
}
