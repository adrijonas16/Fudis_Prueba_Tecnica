using Fudis.Pagos.Api.Configuration;
using Fudis.Pagos.Api.Services;

namespace Fudis.Pagos.Api.Workers;

/// <summary>
/// Proceso que corre en segundo plano mientras la API está encendida.
/// Cada 2 segundos le pide al EnvioNotificacionesService que envíe los mensajes pendientes.
/// Pueden correr varias copias a la vez (varios servidores) sin enviar el mismo mensaje dos veces.
/// </summary>
public sealed class EnvioNotificacionesWorker : BackgroundService
{
    private readonly IServiceScopeFactory _fabricaDeAlcances;
    private readonly EnvioNotificacionesOptions _opciones;
    private readonly ILogger<EnvioNotificacionesWorker> _logger;

    public EnvioNotificacionesWorker(
        IServiceScopeFactory fabricaDeAlcances,
        EnvioNotificacionesOptions opciones,
        ILogger<EnvioNotificacionesWorker> logger)
    {
        _fabricaDeAlcances = fabricaDeAlcances;
        _opciones = opciones;
        _logger = logger;
    }

    /// <summary>Lo ejecuta ASP.NET al arrancar la API. Corre en un ciclo hasta que la API se apaga.</summary>
    protected override async Task ExecuteAsync(CancellationToken alApagar)
    {
        if (!_opciones.Activado)
        {
            _logger.LogInformation("El envío de notificaciones está desactivado por configuración.");
            return;
        }

        // Repite hasta que la API se apague.
        while (!alApagar.IsCancellationRequested)
        {
            int mensajesTomados = 0;
            try
            {
                // Cada vuelta usa su propio "alcance" (scope), es decir, su propia conexión a la base de datos,
                // igual que si fuera una petición HTTP nueva.
                await using var alcance = _fabricaDeAlcances.CreateAsyncScope();
                var servicio = alcance.ServiceProvider.GetRequiredService<EnvioNotificacionesService>();
                mensajesTomados = await servicio.EnviarPendientesAsync(alApagar);
            }
            catch (OperationCanceledException) when (alApagar.IsCancellationRequested)
            {
                break; // la API se está apagando
            }
            catch (Exception error)
            {
                // Un error no debe detener el trabajador: se registra y se intenta en la siguiente vuelta.
                _logger.LogError(error, "Error enviando notificaciones; se reintenta en la siguiente vuelta.");
            }

            // Si el lote vino lleno, probablemente hay más mensajes: seguir sin esperar.
            bool hayQueEsperar = mensajesTomados < _opciones.TamanoLote;
            if (hayQueEsperar)
            {
                try { await Task.Delay(_opciones.EsperaEntreRevisiones, alApagar); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
