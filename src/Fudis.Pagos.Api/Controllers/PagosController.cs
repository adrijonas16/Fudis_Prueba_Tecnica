using Fudis.Pagos.Api.Filters;
using Fudis.Pagos.Api.Models.Contracts;
using Fudis.Pagos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fudis.Pagos.Api.Controllers;

/// <summary>
/// Endpoint que llama la pasarela de pagos cuando un cliente paga (webhook).
/// El controlador solo hace cosas de HTTP. La firma la verifica el filtro [ValidarFirmaPasarela]
/// y la lógica del pago está en ConfirmacionPagoService.
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
    private readonly ConfirmacionPagoService _confirmacionPagoService;

    /// <summary>ASP.NET entrega el servicio automáticamente.</summary>
    public PagosController(ConfirmacionPagoService confirmacionPagoService) =>
        _confirmacionPagoService = confirmacionPagoService;

    /// <param name="cancelacion">
    /// Lo pasa ASP.NET automáticamente. Se activa si la pasarela se desconecta antes de recibir la respuesta;
    /// en ese caso se detiene el trabajo y la transacción se deshace completa (la pasarela reintentará).
    /// </param>
    [HttpPost("confirm-payment")]
    [RequestSizeLimit(64 * 1024)]
    [Consumes("application/json")]
    [ValidarFirmaPasarela] // si la firma no es válida responde 401 y este método no se ejecuta
    public async Task<IActionResult> ConfirmarPago(CancellationToken cancelacion)
    {
        // 1-2. El filtro ya leyó el JSON tal cual llegó y comprobó la firma. Aquí solo lo recuperamos.
        //      (No usamos [FromBody]: la firma se calcula sobre el texto exacto, no sobre un objeto.)
        string jsonRecibido = ValidarFirmaPasarelaFilter.ObtenerCuerpoFirmado(HttpContext);

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
}
