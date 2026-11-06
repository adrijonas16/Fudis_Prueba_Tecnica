using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Fudis.Pagos.Api.Models.Contracts;
using Fudis.Pagos.Api.Models.Entities;
using Fudis.Pagos.Api.Services;
using Fudis.Pagos.IntegrationTests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fudis.Pagos.IntegrationTests;

/// <summary>Pruebas de punta a punta: se levanta la API real y se le hacen peticiones HTTP.</summary>
[Collection(SqlServerCollection.Nombre)]
public class PagosControllerTests
{
    private const string Secreto = "secreto-de-prueba";
    private const string Ruta = "/api/payments/confirm-payment";
    private readonly SqlServerFixture _servidor;

    public PagosControllerTests(SqlServerFixture servidor) => _servidor = servidor;

    private static WebApplicationFactory<Program> LevantarApi(string cadenaConexion) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Pruebas");
            web.UseSetting("ConnectionStrings:Fudis", cadenaConexion);
            web.UseSetting("FirmaPasarela:Secretos:0", Secreto);
            web.UseSetting("EnvioNotificaciones:Activado", "false"); // el envío se ejecuta a mano en la prueba
        });

    private static string Json(int pedidoId, string eventoId = "evt_http_1", decimal monto = 45000m) => $$"""
        {"eventId":"{{eventoId}}","eventType":"payment.succeeded","paymentId":"pay_http","orderId":{{pedidoId}},
         "amount":{{monto.ToString(CultureInfo.InvariantCulture)}},"currency":"COP","occurredAt":"2026-11-05T19:00:00Z"}
        """;

    private static HttpRequestMessage PeticionFirmada(string json, string secreto = Secreto, DateTimeOffset? momento = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, Ruta) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        peticion.Headers.Add(VerificacionFirmaService.NombreCabecera, VerificacionFirmaService.GenerarCabecera(json, secreto, momento ?? DateTimeOffset.UtcNow));
        return peticion;
    }

    [Fact]
    public async Task Un_aviso_firmado_se_procesa_completo_incluido_el_aviso_al_restaurante()
    {
        var cadena = await _servidor.CrearBaseDeDatosAsync();
        var db = new TestDatabase(cadena);
        var pedidoId = await db.CrearPedidoAsync();
        await using var api = LevantarApi(cadena);
        var cliente = api.CreateClient();

        var respuesta = await cliente.SendAsync(PeticionFirmada(Json(pedidoId)));
        var repetida = await cliente.SendAsync(PeticionFirmada(Json(pedidoId)));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("PROCESADO", (await respuesta.Content.ReadFromJsonAsync<RespuestaConfirmarPago>())!.Resultado);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal("DUPLICADO", (await repetida.Content.ReadFromJsonAsync<RespuestaConfirmarPago>())!.Resultado);

        // El servicio real de envío (con el notificador configurado en la API) entrega el aviso.
        using (var alcance = api.Services.CreateScope())
        {
            var envio = alcance.ServiceProvider.GetRequiredService<EnvioNotificacionesService>();
            Assert.Equal(1, await envio.EnviarPendientesAsync(CancellationToken.None));
        }

        await using var contexto = db.CrearContexto();
        Assert.Equal(1, await contexto.GananciasRepartidor.CountAsync());
        Assert.NotNull((await contexto.MensajesPendientes.SingleAsync()).FechaEnvio);
    }

    [Fact]
    public async Task Sin_firma_o_con_firma_falsa_se_rechaza_sin_tocar_el_pedido()
    {
        var cadena = await _servidor.CrearBaseDeDatosAsync();
        var db = new TestDatabase(cadena);
        var pedidoId = await db.CrearPedidoAsync();
        await using var api = LevantarApi(cadena);
        var cliente = api.CreateClient();
        var json = Json(pedidoId);

        var sinFirma = await cliente.PostAsync(Ruta, new StringContent(json, Encoding.UTF8, "application/json"));
        var firmaFalsa = await cliente.SendAsync(PeticionFirmada(json, secreto: "secreto-adivinado"));
        var firmaVieja = await cliente.SendAsync(PeticionFirmada(json, momento: DateTimeOffset.UtcNow.AddMinutes(-30)));
        var peticionAlterada = PeticionFirmada(json);
        peticionAlterada.Content = new StringContent(Json(pedidoId, monto: 1m), Encoding.UTF8, "application/json");
        var jsonAlterado = await cliente.SendAsync(peticionAlterada);

        Assert.All(new[] { sinFirma, firmaFalsa, firmaVieja, jsonAlterado }, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        await using var contexto = db.CrearContexto();
        Assert.Equal(0, await contexto.NotificacionesPago.CountAsync());
        Assert.Equal(EstadoPedido.PendienteDePago, (await contexto.Pedidos.SingleAsync()).Estado);
    }

    [Theory]
    [InlineData("{esto no es json")]
    [InlineData("""{"orderId":1}""")]
    public async Task Firmado_pero_con_datos_invalidos_responde_400(string json)
    {
        var cadena = await _servidor.CrearBaseDeDatosAsync();
        await using var api = LevantarApi(cadena);

        var respuesta = await api.CreateClient().SendAsync(PeticionFirmada(json));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Quince_peticiones_http_iguales_al_mismo_tiempo_dejan_una_sola_ganancia()
    {
        var cadena = await _servidor.CrearBaseDeDatosAsync();
        var db = new TestDatabase(cadena);
        var pedidoId = await db.CrearPedidoAsync();
        await using var api = LevantarApi(cadena);
        var cliente = api.CreateClient();
        var json = Json(pedidoId, eventoId: "evt_http_carrera");

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => cliente.SendAsync(PeticionFirmada(json))));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await using var contexto = db.CrearContexto();
        Assert.Equal(1, await contexto.GananciasRepartidor.CountAsync());
        Assert.Equal(1, await contexto.MensajesPendientes.CountAsync());
    }

    [Fact]
    public async Task Health_responde_sano_cuando_hay_base_de_datos()
    {
        var cadena = await _servidor.CrearBaseDeDatosAsync();
        await using var api = LevantarApi(cadena);

        var respuesta = await api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Healthy", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_responde_503_cuando_no_hay_base_de_datos()
    {
        var sinServidor = "Server=127.0.0.1,1;Database=NoExiste;User Id=sa;Password=x;TrustServerCertificate=True;Connect Timeout=2";
        await using var api = LevantarApi(sinServidor);

        var respuesta = await api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
    }

    [Fact]
    public void La_api_no_arranca_sin_secretos_de_firma()
    {
        var api = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Pruebas");
            web.UseSetting("ConnectionStrings:Fudis", "Server=x;Database=y;");
            web.UseSetting("EnvioNotificaciones:Activado", "false");
        });

        var error = Assert.ThrowsAny<Exception>(() => api.CreateClient());
        Assert.Contains("FirmaPasarela:Secretos", error.ToString());
    }
}
