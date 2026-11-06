using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fudis.Pagos.Api.Database;

/// <summary>
/// Lo usa el endpoint /health. Si no hay conexión a SQL Server, responde "Unhealthy" para que el balanceador
/// o Kubernetes dejen de mandar pagos a esta instancia (sin base de datos no puede procesarlos).
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private static readonly TimeSpan TiempoMaximo = TimeSpan.FromSeconds(3);
    private readonly FudisDbContext _db;

    public DatabaseHealthCheck(FudisDbContext db) => _db = db;

    /// <summary>Intenta conectarse a SQL Server. Healthy si puede, Unhealthy si no o si tarda más de 3 s.</summary>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancelacion = default)
    {
        try
        {
            // SqlClient puede bloquear el hilo mientras abre la conexión TCP (hasta ~15 s) y no respeta la cancelación.
            // Task.Run lo saca a otro hilo y WaitAsync deja de esperar a los 3 s, aunque la conexión siga intentándolo.
            bool hayConexion = await Task.Run(() => _db.Database.CanConnectAsync(cancelacion), cancelacion)
                .WaitAsync(TiempoMaximo, cancelacion);
            return hayConexion
                ? HealthCheckResult.Healthy("SQL Server disponible.")
                : HealthCheckResult.Unhealthy("No se puede conectar a SQL Server.");
        }
        catch (TimeoutException)
        {
            return HealthCheckResult.Unhealthy($"SQL Server no respondió en {TiempoMaximo.TotalSeconds} s.");
        }
        catch (Exception error)
        {
            return HealthCheckResult.Unhealthy("Error conectando a SQL Server.", error);
        }
    }
}
