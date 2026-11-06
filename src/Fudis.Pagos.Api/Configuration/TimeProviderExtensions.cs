namespace Fudis.Pagos.Api.Configuration;

/// <summary>Ayudas para obtener la hora actual de forma consistente con la base de datos.</summary>
public static class TimeProviderExtensions
{
    /// <summary>
    /// La hora actual en UTC, sin fracciones de milisegundo.
    ///
    /// ¿Por qué quitar la fracción? SQL Server guarda las fechas con milisegundos (datetime2(3)) y
    /// REDONDEA lo que sobra. Si no la quitamos, la hora guardada puede quedar 1 ms en el "futuro"
    /// respecto a la que usamos para comparar, y un mensaje recién creado no se enviaría a tiempo.
    /// (Este error lo encontraron las pruebas.)
    ///
    /// Se usa TimeProvider en vez de DateTime.UtcNow para que las pruebas puedan "adelantar el reloj".
    /// </summary>
    public static DateTime AhoraUtc(this TimeProvider reloj)
    {
        var ticks = reloj.GetUtcNow().UtcTicks;
        var sinFraccion = ticks - ticks % TimeSpan.TicksPerMillisecond;
        return new DateTime(sinFraccion, DateTimeKind.Utc);
    }
}
