using Fudis.Pagos.Api.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;

namespace Fudis.Pagos.IntegrationTests.Helpers;

/// <summary>
/// Un SQL Server REAL para las pruebas. No se usa una base "en memoria" porque esa no tiene transacciones,
/// bloqueos ni índices únicos, que son justo lo que hay que probar.
///   - Si existe la variable de entorno FUDIS_TEST_SQLSERVER, usa ese servidor.
///   - Si no, levanta un contenedor de Docker automáticamente (Testcontainers) y lo borra al terminar.
/// Cada prueba recibe su propia base de datos nueva, para que no se mezclen datos.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _contenedor;
    private string _conexionAlServidor = "";
    private readonly List<string> _basesCreadas = [];

    public async Task InitializeAsync()
    {
        var servidorExterno = Environment.GetEnvironmentVariable("FUDIS_TEST_SQLSERVER");
        if (!string.IsNullOrWhiteSpace(servidorExterno))
        {
            _conexionAlServidor = servidorExterno;
            return;
        }

        _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _contenedor.StartAsync();
        _conexionAlServidor = _contenedor.GetConnectionString();
    }

    /// <summary>Crea una base de datos vacía, le aplica las migraciones y devuelve su cadena de conexión.</summary>
    /// <param name="incluirScript">Para crear una base solo con el esquema ORIGINAL (script 000) y probar el código viejo.</param>
    public async Task<string> CrearBaseDeDatosAsync(Func<string, bool>? incluirScript = null)
    {
        var nombre = "fudis_prueba_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conexion = new SqlConnection(_conexionAlServidor))
        {
            await conexion.OpenAsync();
            await using var comando = new SqlCommand($"CREATE DATABASE [{nombre}]", conexion);
            await comando.ExecuteNonQueryAsync();
        }
        _basesCreadas.Add(nombre);

        var cadenaConexion = new SqlConnectionStringBuilder(_conexionAlServidor)
        {
            InitialCatalog = nombre,
            TrustServerCertificate = true,
            MaxPoolSize = 200
        }.ConnectionString;

        await new DatabaseMigrator(NullLogger<DatabaseMigrator>.Instance).AplicarMigracionesAsync(cadenaConexion, incluirScript);
        return cadenaConexion;
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        if (_contenedor is not null)
        {
            await _contenedor.DisposeAsync();
            return;
        }

        await using var conexion = new SqlConnection(_conexionAlServidor);
        await conexion.OpenAsync();
        foreach (var nombre in _basesCreadas)
        {
            await using var comando = new SqlCommand(
                $"ALTER DATABASE [{nombre}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{nombre}];", conexion);
            await comando.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>Todas las clases de prueba con [Collection("SqlServer")] comparten el mismo servidor.</summary>
[CollectionDefinition(Nombre)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Nombre = "SqlServer";
}
