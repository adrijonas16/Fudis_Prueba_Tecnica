using System.Text;
using Fudis.Pagos.Api.Models.Contracts;
using Fudis.Pagos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fudis.Pagos.Api.Controllers;

/// <summary>
/// Endpoint que llama la pasarela de pagos cuando un cliente paga (webhook).
/// El controlador solo hace cosas de HTTP. La lógica del pago está en ConfirmacionPagoService.
///
/// Respuestas:
///   200 -> aviso recibido y registrado (procesado, repetido o marcado para revisión). La pasarela no reintenta.
///   400 -> el JSON está mal o le faltan datos.
///   401 -> la firma no es válida: el aviso no viene de la pasarela.
///   500 -> error inesperado. No quedó nada a medias, así que la pasarela puede reintentar sin riesgo.
/// </summary>
[ApiController]
[Route("api/payments")]
[AllowAnonymous] // No hay usuario: la seguridad la da la firma.
public sealed class PagosController : ControllerBase
{
    private readonly VerificacionFirmaService _verificacionFirmaService;
    private readonly ConfirmacionPagoService _confirmacionPagoService;
    private readonly ILogger<PagosController> _logger;

    public PagosController(
        VerificacionFirmaService verificacionFirmaService,
        ConfirmacionPagoService confirmacionPagoService,
        ILogger<PagosController> logger)
    {
        _verificacionFirmaService = verificacionFirmaService;
        _confirmacionPagoService = confirmacionPagoService;
        _logger = logger;
    }

    /// <param name="cancelacion">
    /// Lo pasa ASP.NET automáticamente. Se activa si la pasarela se desconecta antes de recibir la respuesta;
    /// en ese caso se detiene el trabajo y la transacción se deshace completa (la pasarela reintentará).
    /// </param>
    [HttpPost("confirm-payment")]
    [RequestSizeLimit(64 * 1024)]
    [Consumes("application/json")]
    public async Task<IActionResult> ConfirmarPago(CancellationToken cancelacion)
    {
        // 1. Leer el JSON TAL CUAL llegó, como texto.
        //    No usamos [FromBody] porque la firma se calcula sobre el texto exacto. Si ASP.NET lo convirtiera
        //    primero en objeto, se perdería el texto original y la firma no se podría comprobar.
        string jsonRecibido = await LeerCuerpoComoTextoAsync(cancelacion);

        // 2. Comprobar la firma: ¿lo mandó de verdad la pasarela?
        string? cabeceraFirma = Request.Headers[VerificacionFirmaService.NombreCabecera];
        ResultadoFirma firma = _verificacionFirmaService.Verificar(jsonRecibido, cabeceraFirma);
        if (firma != ResultadoFirma.Valida)
        {
            _logger.LogWarning("Aviso de pago rechazado. Firma: {Firma}. IP: {Ip}", firma, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        // 3. Convertir el texto JSON en objeto.
        SolicitudConfirmarPago? solicitud = SolicitudConfirmarPago.DesdeJson(jsonRecibido);
        if (solicitud is null)
            return ValidationProblem(new ValidationProblemDetails { Detail = "El JSON está mal formado o vacío." });

        // 4. Revisar que no falten datos.
        var errores = solicitud.Validar();
        if (errores.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(errores));

        // 5. Todo correcto: el servicio hace el trabajo y respondemos qué pasó.
        PagoRecibido pago = solicitud.ConvertirAPagoRecibido(jsonRecibido);
        ResultadoConfirmacion resultado = await _confirmacionPagoService.ConfirmarPagoAsync(pago, cancelacion);

        return Ok(new RespuestaConfirmarPago(resultado.Codigo, resultado.RequiereRevision, resultado.Detalle));
    }

    /// <summary>Lee el cuerpo de la petición como texto, sin modificarlo (necesario para comprobar la firma).</summary>
    private async Task<string> LeerCuerpoComoTextoAsync(CancellationToken cancelacion)
    {
        using var lector = new StreamReader(Request.Body, Encoding.UTF8);
        return await lector.ReadToEndAsync(cancelacion);
    }
}
