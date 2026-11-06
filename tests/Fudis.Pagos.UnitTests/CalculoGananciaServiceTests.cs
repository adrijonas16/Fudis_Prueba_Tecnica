using System.Globalization;
using Fudis.Pagos.Api.Services;

namespace Fudis.Pagos.UnitTests;

/// <summary>
/// Pruebas de la regla del 70 % y su redondeo. No usan base de datos.
///
/// Convención: cada prueba se llama como la regla que comprueba, en español y con guiones bajos,
/// para que en el Explorador de pruebas se lea como una lista de reglas de negocio.
/// </summary>
public class CalculoGananciaServiceTests
{
    [Theory]
    [InlineData("5000", "3500")]
    [InlineData("0", "0")]
    [InlineData("10.01", "7.01")]     // 7,007   -> 7,01
    [InlineData("0.05", "0.04")]      // 0,035   -> 0,04
    [InlineData("0.15", "0.11")]      // 0,105   -> 0,11 (redondeo "normal" daría 0,10)
    [InlineData("3333.33", "2333.33")]
    public void Calcula_el_70_por_ciento_redondeado_a_centavos(string tarifaEnvio, string gananciaEsperada)
    {
        var calculadora = new CalculoGananciaService();

        var ganancia = calculadora.Calcular(decimal.Parse(tarifaEnvio, CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(gananciaEsperada, CultureInfo.InvariantCulture), ganancia);
    }

    [Fact]
    public void Usa_el_porcentaje_configurado()
    {
        Assert.Equal(80m, new CalculoGananciaService(80m).Calcular(100m));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void Rechaza_porcentajes_invalidos(double porcentaje)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CalculoGananciaService((decimal)porcentaje));
    }

    [Fact]
    public void Rechaza_tarifas_negativas()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CalculoGananciaService().Calcular(-0.01m));
    }
}
