using Fudis.Pagos.Api.Database;
using Fudis.Pagos.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fudis.Pagos.Api.Repositories;

/// <summary>Acceso a la tabla de avisos recibidos de la pasarela (PaymentNotifications).</summary>
public sealed class NotificacionPagoRepository
{
    private readonly FudisDbContext _db;

    public NotificacionPagoRepository(FudisDbContext db) => _db = db;

    /// <summary>
    /// Intenta guardar el aviso. Devuelve:
    ///   true  -> es la primera vez que llega este aviso.
    ///   false -> este aviso (misma Pasarela + EventoId) YA había llegado antes. Es un repetido.
    ///
    /// ¿Cómo sabe que es repetido? La tabla tiene un índice ÚNICO en (Provider, EventId).
    /// SQL Server rechaza el segundo INSERT y aquí reconocemos ese error.
    /// Si llegan dos iguales al mismo tiempo, SQL Server hace esperar al segundo hasta que el primero
    /// termine; luego el segundo recibe el error de duplicado. Nunca pasan los dos.
    /// </summary>
    public async Task<bool> IntentarRegistrarAsync(NotificacionPago notificacion, CancellationToken cancelacion)
    {
        _db.NotificacionesPago.Add(notificacion);
        try
        {
            await _db.SaveChangesAsync(cancelacion);
            return true;
        }
        catch (DbUpdateException error) when (SqlServerErrors.EsRegistroDuplicado(error))
        {
            return false;
        }
    }

    /// <summary>Busca cómo terminó un aviso que ya habíamos recibido (para informarlo en la respuesta).</summary>
    public Task<string?> BuscarResultadoAsync(string pasarela, string eventoId, CancellationToken cancelacion) =>
        _db.NotificacionesPago.AsNoTracking()
            .Where(n => n.Pasarela == pasarela && n.EventoId == eventoId)
            .Select(n => n.Resultado)
            .FirstOrDefaultAsync(cancelacion);
}
