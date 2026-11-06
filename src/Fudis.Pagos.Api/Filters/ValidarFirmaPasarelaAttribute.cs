using System.Text;
using Fudis.Pagos.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Fudis.Pagos.Api.Filters;

/// <summary>
/// Se pone encima de un endpoint: [ValidarFirmaPasarela].
/// Antes de que se ejecute el método del controlador, comprueba que el aviso viene de la pasarela.
/// Si la firma no es válida responde 401 y el controlador ni siquiera se ejecuta.
/// </summary>
public sealed class ValidarFirmaPasarelaAttribute : TypeFilterAttribute
{
    /// <summary>
    /// Le dice a ASP.NET qué filtro crear (con sus dependencias inyectadas).
    /// Order = 1000: corre DESPUÉS de [RequestSizeLimit] (900) y [Consumes], para que el límite de tamaño
    /// y el tipo de contenido se apliquen antes de leer el cuerpo.
    /// </summary>
    public ValidarFirmaPasarelaAttribute() : base(typeof(ValidarFirmaPasarelaFilter)) => Order = 1000;
}

/// <summary>
/// La lógica del filtro. Es un "resource filter": corre ANTES de que ASP.NET lea el cuerpo de la petición,
/// así podemos leerlo nosotros tal cual llegó (la firma se calcula sobre el texto exacto).
/// </summary>
public sealed class ValidarFirmaPasarelaFilter : IAsyncResourceFilter
{
    private const string ClaveCuerpo = "Fudis.CuerpoFirmado";

    private readonly VerificacionFirmaService _verificacionFirmaService;
    private readonly ILogger<ValidarFirmaPasarelaFilter> _logger;

    /// <summary>ASP.NET entrega las dependencias automáticamente.</summary>
    public ValidarFirmaPasarelaFilter(VerificacionFirmaService verificacionFirmaService, ILogger<ValidarFirmaPasarelaFilter> logger)
    {
        _verificacionFirmaService = verificacionFirmaService;
        _logger = logger;
    }

    /// <summary>Lee el cuerpo, verifica la firma y, si es válida, deja el cuerpo guardado para el controlador.</summary>
    public async Task OnResourceExecutionAsync(ResourceExecutingContext contexto, ResourceExecutionDelegate siguiente)
    {
        var http = contexto.HttpContext;

        string cuerpo;
        using (var lector = new StreamReader(http.Request.Body, Encoding.UTF8))
            cuerpo = await lector.ReadToEndAsync(http.RequestAborted);

        string? cabecera = http.Request.Headers[VerificacionFirmaService.NombreCabecera];
        ResultadoFirma firma = _verificacionFirmaService.Verificar(cuerpo, cabecera);

        if (firma != ResultadoFirma.Valida)
        {
            _logger.LogWarning("Aviso de pago rechazado. Firma: {Firma}. IP: {Ip}", firma, http.Connection.RemoteIpAddress);
            contexto.Result = new UnauthorizedResult(); // corta aquí: el controlador no se ejecuta
            return;
        }

        http.Items[ClaveCuerpo] = cuerpo; // el controlador lo recupera con ObtenerCuerpoFirmado
        await siguiente();
    }

    /// <summary>
    /// Devuelve el cuerpo ya verificado. Solo funciona en endpoints marcados con [ValidarFirmaPasarela];
    /// si se olvida el atributo, falla de inmediato en vez de procesar un aviso sin verificar.
    /// </summary>
    public static string ObtenerCuerpoFirmado(HttpContext http) =>
        http.Items[ClaveCuerpo] as string
        ?? throw new InvalidOperationException("Este endpoint necesita el atributo [ValidarFirmaPasarela].");
}
