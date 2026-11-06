using Fudis.Pagos.Api.Database;
using Fudis.Pagos.Api.Models.Contracts;
using Fudis.Pagos.IntegrationTests.Helpers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fudis.Pagos.IntegrationTests;

/// <summary>
/// Pruebas de los scripts SQL de db/migrations: que no destruyan datos, que se detengan si hay
/// datos inválidos y que se puedan ejecutar más de una vez.
/// </summary>
[Collection(SqlServerCollection.Nombre)]
public class DatabaseMigratorTests
{
    private readonly SqlServerFixture _servidor;
    public DatabaseMigratorTests(SqlServerFixture servidor) => _servidor = servidor;

    private static readonly DatabaseMigrator Migrador = new(NullLogger<DatabaseMigrator>.Instance);

    private Task<string> BaseConEsquemaOriginalAsync() =>
        _servidor.CrearBaseDeDatosAsync(script => script.StartsWith("000", StringComparison.Ordinal));

    [Fact]
    public async Task La_migracion_se_detiene_sin_cambiar_nada_si_hay_ganancias_duplicadas()
    {
        var cadena = await BaseConEsquemaOriginalAsync();
        await EjecutarAsync(cadena, """
            INSERT INTO dbo.Orders (Status, DriverId, DeliveryFee) VALUES ('PAID', 7, 5000);
            INSERT INTO dbo.DriverEarnings (OrderId, DriverId, Amount) VALUES (1, 7, 3500), (1, 7, 3500);
            """);

        var error = await Assert.ThrowsAsync<SqlException>(() => Migrador.AplicarMigracionesAsync(cadena));

        Assert.Equal(50001, error.Number); // el THROW 50001 del script
        Assert.Equal(0, await ContarAsync(cadena, "SELECT COUNT(*) FROM sys.tables WHERE name = 'PaymentNotifications'"));
        Assert.Equal(2, await ContarAsync(cadena, "SELECT COUNT(*) FROM dbo.DriverEarnings")); // no se borró nada
    }

    [Fact]
    public async Task La_migracion_conserva_los_datos_y_se_puede_ejecutar_dos_veces()
    {
        var cadena = await BaseConEsquemaOriginalAsync();
        await EjecutarAsync(cadena, """
            INSERT INTO dbo.Orders (Status, DriverId, DeliveryFee) VALUES ('PAID', 7, 5000), ('PENDING_PAYMENT', NULL, 4000);
            INSERT INTO dbo.DriverEarnings (OrderId, DriverId, Amount) VALUES (1, 7, 3500);
            """);

        await Migrador.AplicarMigracionesAsync(cadena);
        await EjecutarAsync(cadena, "DELETE FROM dbo.SchemaVersions"); // obliga a ejecutar todo otra vez
        await Migrador.AplicarMigracionesAsync(cadena);

        Assert.Equal(2, await ContarAsync(cadena, "SELECT COUNT(*) FROM dbo.Orders"));
        Assert.Equal(1, await ContarAsync(cadena, "SELECT COUNT(*) FROM dbo.DriverEarnings"));
        Assert.Equal(1, await ContarAsync(cadena, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_DriverEarnings_OrderId'"));
    }

    [Fact]
    public async Task Un_reintento_para_un_pedido_pagado_por_el_codigo_viejo_se_marca_para_revision()
    {
        var cadena = await BaseConEsquemaOriginalAsync();
        await EjecutarAsync(cadena, """
            INSERT INTO dbo.Orders (Status, DriverId, DeliveryFee) VALUES ('PAID', 7, 5000);
            INSERT INTO dbo.DriverEarnings (OrderId, DriverId, Amount) VALUES (1, 7, 3500);
            """);
        await Migrador.AplicarMigracionesAsync(cadena);
        var db = new TestDatabase(cadena);

        var resultado = await db.ConfirmarAsync(TestDatabase.Pago(pedidoId: 1));

        Assert.Equal(TipoResultado.PedidoNoPagable, resultado.Tipo);
        Assert.Equal(1, await ContarAsync(cadena, "SELECT COUNT(*) FROM dbo.DriverEarnings"));
    }

    private static async Task EjecutarAsync(string cadena, string sql)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task<int> ContarAsync(string cadena, string sql)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        return (int)(await comando.ExecuteScalarAsync())!;
    }
}
