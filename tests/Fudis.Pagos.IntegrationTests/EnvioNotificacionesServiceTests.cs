using System.Collections.Concurrent;
using Fudis.Pagos.Api.Configuration;
using Fudis.Pagos.Api.Notifications;
using Fudis.Pagos.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Fudis.Pagos.IntegrationTests;

/// <summary>
/// Pruebas del envío de avisos al restaurante (Outbox): reintentos, tiempo límite, descarte,
/// trabajador caído y varios trabajadores en paralelo. Usan un notificador falso que puede fallar a propósito.
/// </summary>
[Collection(SqlServerCollection.Nombre)]
public class EnvioNotificacionesServiceTests
{
    private readonly SqlServerFixture _servidor;
    public EnvioNotificacionesServiceTests(SqlServerFixture servidor) => _servidor = servidor;

    private static readonly EnvioNotificacionesOptions Opciones = new()
    {
        TamanoLote = 10,
        MaximoIntentos = 3,
        EsperaInicial = TimeSpan.FromSeconds(10),
        EsperaMaxima = TimeSpan.FromMinutes(5),
        TiempoDeBloqueo = TimeSpan.FromSeconds(60)
    };

    private async Task<TestDatabase> BaseConUnPedidoPagadoAsync(string pagoId = "pay_1")
    {
        var db = new TestDatabase(await _servidor.CrearBaseDeDatosAsync());
        var pedidoId = await db.CrearPedidoAsync();
        await db.ConfirmarAsync(TestDatabase.Pago(pedidoId, pagoId: pagoId));
        return db;
    }

    [Fact]
    public async Task Avisa_al_restaurante_una_vez_con_el_id_del_mensaje()
    {
        var db = await BaseConUnPedidoPagadoAsync(pagoId: "pay_n");
        var notificador = new NotificadorDePrueba();

        Assert.Equal(1, await db.EnviarNotificacionesAsync(notificador, Opciones));
        Assert.Equal(0, await db.EnviarNotificacionesAsync(notificador, Opciones)); // ya no queda nada

        var aviso = Assert.Single(notificador.AvisosRecibidos);
        Assert.Equal("pay_n", aviso.PagoId);
        await using var contexto = db.CrearContexto();
        var mensaje = await contexto.MensajesPendientes.SingleAsync();
        Assert.Equal(mensaje.Id, aviso.MensajeId);
        Assert.NotNull(mensaje.FechaEnvio);
        Assert.Equal(1, mensaje.Intentos);
    }

    [Fact]
    public async Task Si_falla_reintenta_esperando_cada_vez_mas_hasta_lograrlo()
    {
        var db = await BaseConUnPedidoPagadoAsync();
        var notificador = new NotificadorDePrueba(fallosAntesDeFuncionar: 2);

        Assert.Equal(1, await db.EnviarNotificacionesAsync(notificador, Opciones)); // intento 1: falla
        Assert.Equal(0, await db.EnviarNotificacionesAsync(notificador, Opciones)); // espera 10 s
        db.Reloj.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal(1, await db.EnviarNotificacionesAsync(notificador, Opciones)); // intento 2: falla -> espera 20 s
        db.Reloj.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal(0, await db.EnviarNotificacionesAsync(notificador, Opciones)); // todavía esperando
        db.Reloj.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, await db.EnviarNotificacionesAsync(notificador, Opciones)); // intento 3: funciona

        Assert.Single(notificador.AvisosRecibidos);
        await using var contexto = db.CrearContexto();
        var mensaje = await contexto.MensajesPendientes.SingleAsync();
        Assert.Equal(3, mensaje.Intentos);
        Assert.NotNull(mensaje.FechaEnvio);
        Assert.Null(mensaje.UltimoError);
    }

    [Fact]
    public async Task Si_siempre_falla_se_descarta_y_deja_de_intentar()
    {
        var db = await BaseConUnPedidoPagadoAsync();
        var notificador = new NotificadorDePrueba(fallosAntesDeFuncionar: int.MaxValue);

        for (int vuelta = 0; vuelta < 5; vuelta++)
        {
            await db.EnviarNotificacionesAsync(notificador, Opciones);
            db.Reloj.Advance(TimeSpan.FromMinutes(10));
        }

        await using var contexto = db.CrearContexto();
        var mensaje = await contexto.MensajesPendientes.SingleAsync();
        Assert.Equal(Opciones.MaximoIntentos, mensaje.Intentos);
        Assert.NotNull(mensaje.FechaDescarte);
        Assert.Null(mensaje.FechaEnvio);
        Assert.Contains("caído", mensaje.UltimoError);
    }

    [Fact]
    public async Task Si_el_restaurante_no_responde_se_corta_por_tiempo_y_cuenta_como_fallo()
    {
        var db = await BaseConUnPedidoPagadoAsync();
        var opcionesConLimiteCorto = new EnvioNotificacionesOptions { TiempoMaximoPorEnvio = TimeSpan.FromMilliseconds(300) };
        var notificadorColgado = new NotificadorDePrueba(demora: TimeSpan.FromMinutes(5)); // nunca responde a tiempo

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        await db.EnviarNotificacionesAsync(notificadorColgado, opcionesConLimiteCorto);
        cronometro.Stop();

        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(10), "El envío debió cortarse por tiempo, no quedarse esperando.");
        await using var contexto = db.CrearContexto();
        var mensaje = await contexto.MensajesPendientes.SingleAsync();
        Assert.Null(mensaje.FechaEnvio);
        Assert.Equal(1, mensaje.Intentos);
        Assert.Null(mensaje.BloqueadoHasta);           // quedó libre para reintentar
        Assert.StartsWith("Tiempo agotado", mensaje.UltimoError);
    }

    [Fact]
    public async Task Un_mensaje_apartado_por_un_trabajador_que_se_cayo_se_retoma_al_vencer_el_bloqueo()
    {
        var db = await BaseConUnPedidoPagadoAsync();
        await using (var contexto = db.CrearContexto())
        {
            // Simulamos un trabajador que apartó el mensaje y se cayó sin terminar.
            var mensaje = await contexto.MensajesPendientes.SingleAsync();
            mensaje.BloqueadoHasta = db.Reloj.GetUtcNow().UtcDateTime.AddSeconds(60);
            await contexto.SaveChangesAsync();
        }
        var notificador = new NotificadorDePrueba();

        Assert.Equal(0, await db.EnviarNotificacionesAsync(notificador, Opciones)); // está apartado
        db.Reloj.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(1, await db.EnviarNotificacionesAsync(notificador, Opciones)); // venció: se retoma
        Assert.Single(notificador.AvisosRecibidos);
    }

    [Fact]
    public async Task Cuatro_trabajadores_a_la_vez_envian_cada_mensaje_una_sola_vez()
    {
        var db = new TestDatabase(await _servidor.CrearBaseDeDatosAsync());
        const int cantidadPedidos = 60;
        for (int i = 0; i < cantidadPedidos; i++)
            await db.ConfirmarAsync(TestDatabase.Pago(await db.CrearPedidoAsync(), pagoId: $"pay_{i}"));
        var notificador = new NotificadorDePrueba(demora: TimeSpan.FromMilliseconds(5));

        var trabajadores = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (await db.EnviarNotificacionesAsync(notificador, Opciones) > 0) { }
        }));
        await Task.WhenAll(trabajadores);

        Assert.Equal(cantidadPedidos, notificador.AvisosRecibidos.Count);
        Assert.Equal(cantidadPedidos, notificador.AvisosRecibidos.Select(a => a.MensajeId).Distinct().Count());
        await using var contexto = db.CrearContexto();
        Assert.Equal(cantidadPedidos, await contexto.MensajesPendientes.CountAsync(m => m.FechaEnvio != null));
    }

    /// <summary>Notificador falso: guarda lo que recibe y puede fallar a propósito.</summary>
    private sealed class NotificadorDePrueba(int fallosAntesDeFuncionar = 0, TimeSpan demora = default) : INotificadorRestaurante
    {
        private int _llamadas;
        public ConcurrentBag<AvisoPedidoPagado> AvisosRecibidos { get; } = [];

        public async Task AvisarPedidoPagadoAsync(AvisoPedidoPagado aviso, CancellationToken cancelacion)
        {
            if (demora > TimeSpan.Zero)
                await Task.Delay(demora, cancelacion);

            if (Interlocked.Increment(ref _llamadas) <= fallosAntesDeFuncionar)
                throw new HttpRequestException("Servicio de notificaciones caído");

            AvisosRecibidos.Add(aviso);
        }
    }
}
