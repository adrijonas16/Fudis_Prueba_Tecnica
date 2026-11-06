using System.Data;
using Fudis.Pagos.Api.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fudis.Pagos.Api.Repositories;

/// <summary>
/// Punto único de acceso a la base de datos para los servicios.
///
/// 1. Expone TODOS los repositorios:  _unitOfWork.Pedidos, _unitOfWork.Ganancias, ...
///    Así cada servicio recibe un solo objeto en vez de un repositorio por tabla.
///
/// 2. Todos los repositorios comparten el MISMO FudisDbContext (uno por petición HTTP). Por eso, si se abre
///    una transacción aquí, todo lo que hagan los repositorios queda dentro de ella: o se confirma todo junto,
///    o no se guarda nada.
/// </summary>
public sealed class UnitOfWork
{
    private readonly FudisDbContext _db;

    public UnitOfWork(FudisDbContext db)
    {
        _db = db;
        Pedidos = new PedidoRepository(db);
        Ganancias = new GananciaRepository(db);
        NotificacionesPago = new NotificacionPagoRepository(db);
        MensajesPendientes = new MensajePendienteRepository(db);
    }

    // ---- Repositorios (uno por tabla) ----
    public PedidoRepository Pedidos { get; }
    public GananciaRepository Ganancias { get; }
    public NotificacionPagoRepository NotificacionesPago { get; }
    public MensajePendienteRepository MensajesPendientes { get; }

    // ---- Transacción ----

    /// <summary>
    /// Ejecuta un trabajo y, si la base de datos tiene un fallo pasajero (ej. se cortó la red un segundo),
    /// lo vuelve a intentar desde el principio (hasta 3 veces, según DependencyInjection.cs).
    /// </summary>
    public Task<T> EjecutarConReintentosAsync<T>(Func<Task<T>> trabajo)
    {
        var estrategia = _db.Database.CreateExecutionStrategy();
        return estrategia.ExecuteAsync(trabajo);
    }

    /// <summary>Abre una transacción. Nada es definitivo hasta llamar a CommitAsync.</summary>
    public Task<IDbContextTransaction> IniciarTransaccionAsync(CancellationToken cancelacion) =>
        _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancelacion);

    /// <summary>Guarda en la base de datos todo lo que los repositorios agregaron o cambiaron.</summary>
    public Task GuardarCambiosAsync(CancellationToken cancelacion) => _db.SaveChangesAsync(cancelacion);

    /// <summary>Olvida todo lo que EF tenía en memoria (se usa al reintentar o tras un error).</summary>
    public void LimpiarMemoria() => _db.ChangeTracker.Clear();
}
