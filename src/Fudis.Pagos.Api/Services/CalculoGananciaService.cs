namespace Fudis.Pagos.Api.Services;

/// <summary>
/// Regla de negocio: el repartidor gana un porcentaje de la tarifa de envío (70 % por defecto).
/// Está en su propia clase para que, si el porcentaje o el redondeo cambian, haya un solo lugar que tocar.
/// </summary>
public sealed class CalculoGananciaService
{
    public CalculoGananciaService(decimal porcentaje = 70m)
    {
        if (porcentaje < 0m || porcentaje > 100m)
            throw new ArgumentOutOfRangeException(nameof(porcentaje), porcentaje, "El porcentaje debe estar entre 0 y 100.");

        Porcentaje = porcentaje;
    }

    public decimal Porcentaje { get; }

    /// <summary>
    /// Calcula la ganancia y la redondea a 2 decimales.
    /// Si queda justo en la mitad (ej. 0,105) se redondea hacia arriba (0,11): MidpointRounding.AwayFromZero.
    /// (Supuesto: así liquida finanzas. Hay que confirmarlo.)
    /// </summary>
    public decimal Calcular(decimal tarifaEnvio)
    {
        if (tarifaEnvio < 0m)
            throw new ArgumentOutOfRangeException(nameof(tarifaEnvio), tarifaEnvio, "La tarifa de envío no puede ser negativa.");

        var ganancia = tarifaEnvio * Porcentaje / 100m;
        return Math.Round(ganancia, 2, MidpointRounding.AwayFromZero);
    }
}
