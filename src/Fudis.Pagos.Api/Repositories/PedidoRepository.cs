using Fudis.Pagos.Api.Database;
using Fudis.Pagos.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fudis.Pagos.Api.Repositories;

/// <summary>Acceso a la tabla de pedidos (Orders).</summary>
public sealed class PedidoRepository
{
    private readonly FudisDbContext _db;

    public PedidoRepository(FudisDbContext db) => _db = db;

    /// <summary>Busca un pedido por su Id. Devuelve null si no existe. Solo lectura (AsNoTracking).</summary>
    public Task<Pedido?> BuscarPorIdAsync(int pedidoId, CancellationToken cancelacion) =>
        _db.Pedidos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pedidoId, cancelacion);

    /// <summary>
    /// Marca el pedido como PAGADO, pero SOLO si:
    ///   - está pendiente de pago,
    ///   - el monto coincide con lo que se pagó,
    ///   - y la moneda coincide.
    ///
    /// Devuelve true si lo marcó, false si no se cumplió alguna condición.
    ///
    /// IMPORTANTE: esto se ejecuta como UNA sola sentencia en SQL Server:
    ///   UPDATE Orders SET Status='PAID', PaymentId=..., PaidAt=...
    ///   WHERE Id=... AND Status='PENDING_PAYMENT' AND TotalAmount=... AND Currency=...
    ///
    /// ¿Por qué así y no "leer el pedido, preguntar con un if y después guardar" como el código original?
    /// Porque si llegan dos avisos al mismo tiempo, los dos leerían "pendiente" y los dos pagarían.
    /// Con un solo UPDATE, SQL Server pone al segundo en fila: cuando le toca, el pedido ya está PAGADO,
    /// el WHERE no se cumple y no cambia nada. Solo uno puede ganar.
    /// </summary>
    public async Task<bool> IntentarMarcarComoPagadoAsync(
        int pedidoId, string pagoId, decimal montoPagado, string moneda, DateTime fechaPago, CancellationToken cancelacion)
    {
        int filasCambiadas = await _db.Pedidos
            .Where(p => p.Id == pedidoId
                        && p.Estado == EstadoPedido.PendienteDePago
                        && p.MontoTotal == montoPagado
                        && p.Moneda == moneda)
            .ExecuteUpdateAsync(cambios => cambios
                .SetProperty(p => p.Estado, EstadoPedido.Pagado)
                .SetProperty(p => p.PagoId, pagoId)
                .SetProperty(p => p.FechaPago, fechaPago),
                cancelacion);

        return filasCambiadas == 1;
    }
}
