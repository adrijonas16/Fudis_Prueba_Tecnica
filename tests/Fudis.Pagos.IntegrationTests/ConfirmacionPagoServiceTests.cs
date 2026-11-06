using Fudis.Pagos.Api.Database;
using Fudis.Pagos.Api.Models.Contracts;
using Fudis.Pagos.Api.Models.Entities;
using Fudis.Pagos.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using static Fudis.Pagos.IntegrationTests.Helpers.TestDatabase;

namespace Fudis.Pagos.IntegrationTests;

/// <summary>
/// Pruebas del servicio principal contra SQL Server REAL (cada prueba con su propia base nueva).
/// Incluye las pruebas de concurrencia: varios avisos al mismo tiempo deben generar una sola ganancia.
/// </summary>
[Collection(SqlServerCollection.Nombre)]
public class ConfirmacionPagoServiceTests
{
    private readonly SqlServerFixture _servidor;
    public ConfirmacionPagoServiceTests(SqlServerFixture servidor) => _servidor = servidor;

    private async Task<TestDatabase> NuevaBaseAsync() => new(await _servidor.CrearBaseDeDatosAsync());

    [Fact]
    public async Task Un_pago_valido_marca_pagado_registra_ganancia_y_deja_aviso_pendiente()
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync(tarifaEnvio: 5000m);

        var resultado = await db.ConfirmarAsync(Pago(pedidoId, pagoId: "pay_ok"));

        Assert.Equal(TipoResultado.Procesado, resultado.Tipo);
        var estado = await db.VerEstadoAsync(pedidoId);
        Assert.Equal(EstadoPedido.Pagado, estado.Pedido.Estado);
        Assert.Equal("pay_ok", estado.Pedido.PagoId);
        Assert.NotNull(estado.Pedido.FechaPago);
        var ganancia = Assert.Single(estado.Ganancias);
        Assert.Equal(3500m, ganancia.Monto);
        Assert.Equal(7, ganancia.RepartidorId);
        Assert.Equal(70m, ganancia.PorcentajeAplicado);
        Assert.Equal("pay_ok", ganancia.PagoOrigenId);
        Assert.Equal(1, estado.MensajesPendientes);
        Assert.Equal(1, estado.Notificaciones);
    }

    [Fact]
    public async Task El_mismo_aviso_dos_veces_se_procesa_una_sola_vez()
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync();
        var pago = Pago(pedidoId, eventoId: "evt_repetido");

        var primero = await db.ConfirmarAsync(pago);
        var segundo = await db.ConfirmarAsync(pago);

        Assert.Equal(TipoResultado.Procesado, primero.Tipo);
        Assert.Equal(TipoResultado.Duplicado, segundo.Tipo);
        Assert.Contains("PROCESADO", segundo.Detalle);
        var estado = await db.VerEstadoAsync(pedidoId);
        Assert.Single(estado.Ganancias);
        Assert.Equal(1, estado.MensajesPendientes);
        Assert.Equal(1, estado.Notificaciones);
    }

    [Fact]
    public async Task El_mismo_aviso_25_veces_al_mismo_tiempo_genera_una_sola_ganancia()
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync();
        var pago = Pago(pedidoId, eventoId: "evt_carrera");

        var resultados = await EjecutarAlMismoTiempoAsync(25, _ => db.ConfirmarAsync(pago));

        Assert.Equal(1, resultados.Count(r => r.Tipo == TipoResultado.Procesado));
        Assert.Equal(24, resultados.Count(r => r.Tipo == TipoResultado.Duplicado));
        var estado = await db.VerEstadoAsync(pedidoId);
        Assert.Single(estado.Ganancias);
        Assert.Equal(1, estado.MensajesPendientes);
        Assert.Equal(1, estado.Notificaciones);
    }

    [Fact]
    public async Task Avisos_distintos_del_mismo_pago_al_mismo_tiempo_generan_una_sola_ganancia()
    {
        // La pasarela puede reintentar con un EventoId nuevo para el mismo pago. Aquí la tabla de avisos
        // no los detecta como repetidos; los detiene el UPDATE condicional del pedido.
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync();

        var resultados = await EjecutarAlMismoTiempoAsync(25, i => db.ConfirmarAsync(Pago(pedidoId, eventoId: $"evt_{i}", pagoId: "pay_mismo")));

        Assert.Equal(1, resultados.Count(r => r.Tipo == TipoResultado.Procesado));
        Assert.Equal(24, resultados.Count(r => r.Tipo == TipoResultado.YaPagado));
        var estado = await db.VerEstadoAsync(pedidoId);
        Assert.Single(estado.Ganancias);
        Assert.Equal(1, estado.MensajesPendientes);
        Assert.Equal(25, estado.Notificaciones); // todos quedan registrados
        Assert.Equal(0, estado.MarcadasParaRevision);
    }

    [Fact]
    public async Task Un_segundo_pago_distinto_para_un_pedido_pagado_se_marca_como_posible_cobro_doble()
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync();
        await db.ConfirmarAsync(Pago(pedidoId, pagoId: "pay_A"));

        var resultado = await db.ConfirmarAsync(Pago(pedidoId, pagoId: "pay_B"));

        Assert.Equal(TipoResultado.PagoEnConflicto, resultado.Tipo);
        Assert.True(resultado.RequiereRevision);
        var estado = await db.VerEstadoAsync(pedidoId);
        Assert.Equal("pay_A", estado.Pedido.PagoId);
        Assert.Single(estado.Ganancias);
        Assert.Equal(1, estado.MarcadasParaRevision);
    }

    [Theory]
    [InlineData(44999.99, "COP")]
    [InlineData(45000.00, "USD")]
    [InlineData(1, "COP")]
    public async Task Si_el_monto_o_la_moneda_no_coinciden_no_se_marca_pagado(decimal monto, string moneda)
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync(montoTotal: 45000m, moneda: "COP");

        var resultado = await db.ConfirmarAsync(Pago(pedidoId, monto: monto, moneda: moneda));

        Assert.Equal(TipoResultado.MontoNoCoincide, resultado.Tipo);
        var estado = await db.VerEstadoAsync(pedidoId);
        Assert.Equal(EstadoPedido.PendienteDePago, estado.Pedido.Estado);
        Assert.Empty(estado.Ganancias);
        Assert.Equal(0, estado.MensajesPendientes);
        Assert.Equal(1, estado.MarcadasParaRevision);
    }

    [Fact]
    public async Task Un_pedido_antiguo_sin_monto_total_no_se_acepta_automaticamente()
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync(montoTotal: null, moneda: null);

        var resultado = await db.ConfirmarAsync(Pago(pedidoId));

        Assert.Equal(TipoResultado.MontoNoCoincide, resultado.Tipo);
        Assert.Equal(EstadoPedido.PendienteDePago, (await db.VerEstadoAsync(pedidoId)).Pedido.Estado);
    }

    [Theory]
    [InlineData(EstadoPedido.Cancelado)]
    [InlineData(EstadoPedido.Reembolsado)]
    public async Task Un_pedido_cancelado_o_reembolsado_no_vuelve_a_quedar_pagado(EstadoPedido estadoInicial)
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync(estado: estadoInicial);

        var resultado = await db.ConfirmarAsync(Pago(pedidoId));

        Assert.Equal(TipoResultado.PedidoNoPagable, resultado.Tipo);
        var estado = await db.VerEstadoAsync(pedidoId);
        Assert.Equal(estadoInicial, estado.Pedido.Estado);
        Assert.Empty(estado.Ganancias);
        Assert.Equal(1, estado.MarcadasParaRevision);
    }

    [Fact]
    public async Task Un_pedido_que_no_existe_queda_registrado_para_revision()
    {
        var db = await NuevaBaseAsync();

        var resultado = await db.ConfirmarAsync(Pago(pedidoId: 999_999));

        Assert.Equal(TipoResultado.PedidoNoExiste, resultado.Tipo);
        await using var contexto = db.CrearContexto();
        var registro = Assert.Single(contexto.NotificacionesPago);
        Assert.True(registro.RequiereRevision);
        Assert.Equal("PEDIDO_NO_EXISTE", registro.Resultado);
    }

    [Fact]
    public async Task Un_aviso_de_pago_fallido_se_registra_y_se_ignora()
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync();

        var resultado = await db.ConfirmarAsync(Pago(pedidoId, tipoEvento: "payment.failed"));

        Assert.Equal(TipoResultado.Ignorado, resultado.Tipo);
        var estado = await db.VerEstadoAsync(pedidoId);
        Assert.Equal(EstadoPedido.PendienteDePago, estado.Pedido.Estado);
        Assert.Empty(estado.Ganancias);
        Assert.Equal(1, estado.Notificaciones);
    }

    [Fact]
    public async Task Un_aviso_de_fallo_que_llega_tarde_no_deshace_el_pago()
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync();
        await db.ConfirmarAsync(Pago(pedidoId, pagoId: "pay_1"));

        var tardio = await db.ConfirmarAsync(Pago(pedidoId, pagoId: "pay_1", tipoEvento: "payment.failed"));

        Assert.Equal(TipoResultado.Ignorado, tardio.Tipo);
        Assert.Equal(EstadoPedido.Pagado, (await db.VerEstadoAsync(pedidoId)).Pedido.Estado);
    }

    [Fact]
    public async Task Un_pedido_sin_repartidor_se_paga_y_se_avisa_pero_la_ganancia_queda_para_despues()
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync(repartidorId: null);

        var resultado = await db.ConfirmarAsync(Pago(pedidoId));

        Assert.Equal(TipoResultado.Procesado, resultado.Tipo);
        Assert.Contains("sin repartidor", resultado.Detalle);
        var estado = await db.VerEstadoAsync(pedidoId);
        Assert.Equal(EstadoPedido.Pagado, estado.Pedido.Estado);
        Assert.Empty(estado.Ganancias);
        Assert.Equal(1, estado.MensajesPendientes);
    }

    [Fact]
    public async Task La_base_de_datos_rechaza_una_segunda_ganancia_aunque_el_codigo_tuviera_un_error()
    {
        var db = await NuevaBaseAsync();
        var pedidoId = await db.CrearPedidoAsync();
        await db.ConfirmarAsync(Pago(pedidoId));

        await using var contexto = db.CrearContexto();
        contexto.GananciasRepartidor.Add(new GananciaRepartidor { PedidoId = pedidoId, RepartidorId = 7, Monto = 3500m, FechaCreacion = DateTime.UtcNow });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
        Assert.True(SqlServerErrors.EsRegistroDuplicado(error));
    }

    /// <summary>Lanza N tareas que arrancan exactamente a la vez, para provocar choques.</summary>
    private static async Task<T[]> EjecutarAlMismoTiempoAsync<T>(int cantidad, Func<int, Task<T>> accion)
    {
        var senalDeSalida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tareas = Enumerable.Range(0, cantidad)
            .Select(i => Task.Run(async () => { await senalDeSalida.Task; return await accion(i); }))
            .ToArray();
        await Task.Delay(100);
        senalDeSalida.SetResult(); // ¡ya!
        return await Task.WhenAll(tareas);
    }
}
