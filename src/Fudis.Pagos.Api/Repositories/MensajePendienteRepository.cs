using System.Data;
using Fudis.Pagos.Api.Database;
using Fudis.Pagos.Api.Models.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Fudis.Pagos.Api.Repositories;

/// <summary>Acceso a la tabla de mensajes pendientes de enviar (OutboxMessages).</summary>
public sealed class MensajePendienteRepository
{
    private readonly FudisDbContext _db;

    public MensajePendienteRepository(FudisDbContext db) => _db = db;

    /// <summary>Prepara un mensaje para guardarse (dentro de la transacción del pago).</summary>
    public void Agregar(MensajePendiente mensaje) => _db.MensajesPendientes.Add(mensaje);

    /// <summary>
    /// "Aparta" hasta <paramref name="cantidad"/> mensajes listos para enviar y los devuelve.
    ///
    /// Apartar = ponerles BloqueadoHasta en el futuro. Mientras tanto, ningún otro trabajador los toma.
    /// Así pueden correr varias copias del sistema a la vez sin enviar el mismo mensaje dos veces.
    ///
    /// Trucos de SQL Server usados:
    ///   UPDLOCK  -> bloquea las filas que voy a cambiar para que nadie más las tome en ese instante.
    ///   READPAST -> si otra copia está apartando una fila, me la salto en vez de quedarme esperando.
    /// Si un trabajador se cae a mitad de envío, cuando vence BloqueadoHasta otro lo vuelve a tomar.
    /// </summary>
    public async Task<List<MensajePendiente>> ApartarLoteAsync(int cantidad, DateTime ahora, DateTime bloqueadoHasta, CancellationToken cancelacion)
    {
        const string sqlApartar = """
            WITH siguientes AS (
                SELECT TOP (@cantidad) Id, LockedUntil
                FROM dbo.OutboxMessages WITH (ROWLOCK, UPDLOCK, READPAST)
                WHERE ProcessedAt IS NULL                                -- no enviado
                  AND DeadLetteredAt IS NULL                             -- no descartado
                  AND NextAttemptAt <= @ahora                            -- ya le toca
                  AND (LockedUntil IS NULL OR LockedUntil < @ahora)      -- nadie lo tiene apartado
                ORDER BY CreatedAt                                       -- los más antiguos primero
            )
            UPDATE siguientes SET LockedUntil = @bloqueadoHasta
            OUTPUT inserted.Id AS [Value];
            """;

        List<Guid> idsApartados = await _db.Database.SqlQueryRaw<Guid>(sqlApartar,
                new SqlParameter("@cantidad", cantidad),
                ParametroFecha("@ahora", ahora),
                ParametroFecha("@bloqueadoHasta", bloqueadoHasta))
            .ToListAsync(cancelacion);

        if (idsApartados.Count == 0)
            return [];

        return await _db.MensajesPendientes
            .Where(m => idsApartados.Contains(m.Id))
            .OrderBy(m => m.FechaCreacion)
            .ToListAsync(cancelacion);
    }

    // Las fechas se envían como datetime2(3), igual que las columnas. Si se mandan como 'datetime'
    // (lo que hace SqlClient por defecto), la precisión no coincide y la comparación puede fallar.
    private static SqlParameter ParametroFecha(string nombre, DateTime valor) =>
        new(nombre, SqlDbType.DateTime2) { Scale = 3, Value = valor };
}
