using Fudis.Pagos.Api.Configuration;
using Fudis.Pagos.Api.Models.Contracts;
using Fudis.Pagos.Api.Models.Entities;

namespace Fudis.Pagos.UnitTests;

/// <summary>
/// Pruebas de reglas sueltas: estados del pedido, qué casos requieren revisión,
/// espera entre reintentos y validación de la configuración.
/// </summary>
public class ReglasNegocioTests
{
    [Fact]
    public void El_estado_pagado_se_guarda_como_PAID_igual_que_el_sistema_actual()
    {
        Assert.Equal("PAID", CodigosEstadoPedido.ATexto(EstadoPedido.Pagado));
        Assert.Equal(EstadoPedido.Pagado, CodigosEstadoPedido.DesdeTexto("PAID"));
    }

    [Fact]
    public void Un_estado_desconocido_falla_en_vez_de_adivinar()
    {
        Assert.Throws<InvalidOperationException>(() => CodigosEstadoPedido.DesdeTexto("paid"));
    }

    [Theory]
    [InlineData(EstadoPedido.PendienteDePago, true)]
    [InlineData(EstadoPedido.Pagado, false)]
    [InlineData(EstadoPedido.Cancelado, false)]
    [InlineData(EstadoPedido.Reembolsado, false)]
    [InlineData(EstadoPedido.Entregado, false)]
    public void Solo_un_pedido_pendiente_se_puede_pagar(EstadoPedido estado, bool sePuedePagar)
    {
        Assert.Equal(sePuedePagar, Pedido.SePuedePagar(estado));
    }

    [Theory]
    [InlineData(TipoResultado.Procesado, false)]
    [InlineData(TipoResultado.Duplicado, false)]
    [InlineData(TipoResultado.YaPagado, false)]
    [InlineData(TipoResultado.Ignorado, false)]
    [InlineData(TipoResultado.PedidoNoExiste, true)]
    [InlineData(TipoResultado.MontoNoCoincide, true)]
    [InlineData(TipoResultado.PedidoNoPagable, true)]
    [InlineData(TipoResultado.PagoEnConflicto, true)]
    public void Los_problemas_de_dinero_requieren_revision(TipoResultado tipo, bool requiereRevision)
    {
        Assert.Equal(requiereRevision, new ResultadoConfirmacion(tipo, "").RequiereRevision);
    }

    [Fact]
    public void La_espera_entre_reintentos_se_duplica_y_tiene_tope()
    {
        var opciones = new EnvioNotificacionesOptions { EsperaInicial = TimeSpan.FromSeconds(5), EsperaMaxima = TimeSpan.FromMinutes(1) };
        Assert.Equal(TimeSpan.FromSeconds(5), opciones.CalcularEspera(1));
        Assert.Equal(TimeSpan.FromSeconds(10), opciones.CalcularEspera(2));
        Assert.Equal(TimeSpan.FromSeconds(40), opciones.CalcularEspera(4));
        Assert.Equal(TimeSpan.FromMinutes(1), opciones.CalcularEspera(5));
        Assert.Equal(TimeSpan.FromMinutes(1), opciones.CalcularEspera(500));
    }

    [Fact]
    public void La_configuracion_por_defecto_del_envio_es_valida()
    {
        new EnvioNotificacionesOptions().Validar(); // no debe lanzar error
    }

    [Theory]
    [InlineData(0, 10, 30, 60)]   // lote vacío
    [InlineData(20, 0, 30, 60)]   // sin intentos
    [InlineData(20, 10, 60, 60)]  // tiempo de envío igual al bloqueo
    [InlineData(20, 10, 90, 60)]  // tiempo de envío mayor al bloqueo
    public void Una_configuracion_de_envio_invalida_impide_arrancar(int lote, int intentos, int segundosEnvio, int segundosBloqueo)
    {
        var opciones = new EnvioNotificacionesOptions
        {
            TamanoLote = lote,
            MaximoIntentos = intentos,
            TiempoMaximoPorEnvio = TimeSpan.FromSeconds(segundosEnvio),
            TiempoDeBloqueo = TimeSpan.FromSeconds(segundosBloqueo)
        };
        Assert.Throws<InvalidOperationException>(() => opciones.Validar());
    }
}
