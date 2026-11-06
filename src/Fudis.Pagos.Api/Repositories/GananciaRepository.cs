using Fudis.Pagos.Api.Database;
using Fudis.Pagos.Api.Models.Entities;

namespace Fudis.Pagos.Api.Repositories;

/// <summary>Acceso a la tabla de ganancias de repartidores (DriverEarnings).</summary>
public sealed class GananciaRepository
{
    private readonly FudisDbContext _db;

    public GananciaRepository(FudisDbContext db) => _db = db;

    /// <summary>
    /// Prepara la ganancia para guardarse. Se guarda de verdad cuando la UnitOfWork llama a
    /// GuardarCambiosAsync, dentro de la misma transacción que el pago.
    ///
    /// Red de seguridad: la tabla tiene un índice ÚNICO por pedido (UX_DriverEarnings_OrderId).
    /// Aunque el código tuviera un error, SQL Server rechazaría una segunda ganancia para el mismo pedido.
    /// </summary>
    public void Agregar(GananciaRepartidor ganancia) => _db.GananciasRepartidor.Add(ganancia);
}
