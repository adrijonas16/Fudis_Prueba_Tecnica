using Fudis.Pagos.Api.Database;
using Fudis.Pagos.Api.Notifications;
using Fudis.Pagos.Api.Repositories;
using Fudis.Pagos.Api.Services;
using Fudis.Pagos.Api.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fudis.Pagos.Api.Configuration;

/// <summary>
/// Aquí se le dice a ASP.NET qué clases existen y cómo crearlas (inyección de dependencias).
///   AddScoped    -> se crea una nueva por cada petición HTTP (todas comparten la misma conexión a BD).
///   AddSingleton -> se crea una sola vez y se reutiliza siempre.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registra todo lo que necesita la API de pagos. Se llama una vez desde Program.cs.</summary>
    public static IServiceCollection AddPagosServices(this IServiceCollection servicios, IConfiguration configuracion)
    {
        // ---- Configuración (desde appsettings.json) ----
        var opcionesPagos = configuracion.GetSection("Pagos").Get<PagosOptions>() ?? new PagosOptions();
        var opcionesFirma = configuracion.GetSection("FirmaPasarela").Get<FirmaOptions>() ?? new FirmaOptions();
        var opcionesEnvio = configuracion.GetSection("EnvioNotificaciones").Get<EnvioNotificacionesOptions>() ?? new EnvioNotificacionesOptions();
        // Si algo obligatorio falta o está mal, la API NO arranca (mejor fallar al desplegar que con el primer pago).
        if (opcionesFirma.Secretos.Count == 0 || opcionesFirma.Secretos.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Falta configurar 'FirmaPasarela:Secretos'. En producción se toma de una variable de entorno o de Key Vault.");
        opcionesEnvio.Validar();

        servicios.AddSingleton(opcionesPagos);
        servicios.AddSingleton(opcionesFirma);
        servicios.AddSingleton(opcionesEnvio);
        servicios.TryAddSingleton(TimeProvider.System); // el reloj real (las pruebas usan uno falso)

        // ---- Base de datos ----
        // En desarrollo viene de appsettings.Development.json. En producción, de la variable de entorno
        // ConnectionStrings__Fudis o de Key Vault: nunca se sube una contraseña real al repositorio.
        var cadenaConexion = configuracion.GetConnectionString("Fudis");
        if (string.IsNullOrWhiteSpace(cadenaConexion))
            throw new InvalidOperationException("Falta 'ConnectionStrings:Fudis' (variable de entorno ConnectionStrings__Fudis en producción).");

        servicios.AddDbContext<FudisDbContext>(opciones => opciones
            .UseSqlServer(cadenaConexion, sql =>
                // Si SQL Server falla un instante, EF reintenta (hasta 3 veces).
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            // Un aviso repetido provoca un error de "registro duplicado" que es ESPERADO y lo manejamos nosotros.
            // EF lo escribiría en el log como error en cada repetido (falsas alarmas); se baja a informativo.
            .ConfigureWarnings(avisos => avisos.Log((CoreEventId.SaveChangesFailed, LogLevel.Information))));

        servicios.AddSingleton<DatabaseMigrator>();

        // ---- Acceso a datos ----
        // Solo se registra el UnitOfWork: él crea los repositorios y todos comparten la misma conexión.
        servicios.AddScoped<UnitOfWork>();

        // ---- Servicios (la lógica) ----
        servicios.AddSingleton(new CalculoGananciaService(opcionesPagos.PorcentajeRepartidor));
        servicios.AddSingleton<VerificacionFirmaService>();
        servicios.AddScoped<ConfirmacionPagoService>();
        servicios.AddScoped<EnvioNotificacionesService>();

        // ---- Notificaciones y trabajador en segundo plano ----
        servicios.TryAddScoped<INotificadorRestaurante, NotificadorRestaurantePorLog>();
        servicios.AddHostedService<EnvioNotificacionesWorker>();

        return servicios;
    }
}
