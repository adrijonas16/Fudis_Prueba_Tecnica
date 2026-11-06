using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Fudis.Pagos.Api.Database;

/// <summary>
/// Ejecuta los scripts de db/migrations en orden (000, 001, ...) y anota cada uno en la tabla
/// dbo.SchemaVersions para no volver a ejecutarlo.
/// Solo se usa en desarrollo y en pruebas. En producción los scripts los ejecuta el DBA o el pipeline.
/// </summary>
public sealed class DatabaseMigrator
{
    // Los scripts SQL se separan en bloques con la palabra GO (igual que en SSMS).
    private static readonly Regex SeparadorGo = new(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(ILogger<DatabaseMigrator> logger) => _logger = logger;

    /// <summary>Crea la base de datos si todavía no existe.</summary>
    public async Task CrearBaseSiNoExisteAsync(string cadenaConexion, CancellationToken cancelacion = default)
    {
        var datosConexion = new SqlConnectionStringBuilder(cadenaConexion);
        var nombreBase = datosConexion.InitialCatalog;

        // Para crear una base hay que conectarse a "master".
        datosConexion.InitialCatalog = "master";
        await using var conexion = new SqlConnection(datosConexion.ConnectionString);
        await conexion.OpenAsync(cancelacion);

        const string sql = "IF DB_ID(@nombre) IS NULL BEGIN DECLARE @sql NVARCHAR(400) = N'CREATE DATABASE ' + QUOTENAME(@nombre); EXEC(@sql); END";
        await using var comando = new SqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("@nombre", nombreBase);
        await comando.ExecuteNonQueryAsync(cancelacion);

        _logger.LogInformation("Base de datos {Base} lista.", nombreBase);
    }

    /// <param name="incluirScript">Opcional: permite ejecutar solo algunos scripts (lo usan las pruebas).</param>
    public async Task AplicarMigracionesAsync(string cadenaConexion, Func<string, bool>? incluirScript = null, CancellationToken cancelacion = default)
    {
        await using var conexion = new SqlConnection(cadenaConexion);
        await conexion.OpenAsync(cancelacion);

        await EjecutarAsync(conexion, """
            IF OBJECT_ID(N'dbo.SchemaVersions') IS NULL
                CREATE TABLE dbo.SchemaVersions (
                    ScriptName NVARCHAR(200) NOT NULL CONSTRAINT PK_SchemaVersions PRIMARY KEY,
                    AppliedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_SchemaVersions_AppliedAt DEFAULT SYSUTCDATETIME());
            """, cancelacion);

        foreach (var nombreScript in ListarScripts())
        {
            if (incluirScript is not null && !incluirScript(nombreScript))
                continue;

            if (await YaFueAplicadoAsync(conexion, nombreScript, cancelacion))
                continue;

            _logger.LogInformation("Aplicando migración {Script}", nombreScript);

            var bloques = SeparadorGo.Split(LeerScript(nombreScript));
            foreach (var bloque in bloques)
            {
                if (!string.IsNullOrWhiteSpace(bloque))
                    await EjecutarAsync(conexion, bloque, cancelacion);
            }

            await using var anotar = new SqlCommand("INSERT INTO dbo.SchemaVersions (ScriptName) VALUES (@n)", conexion);
            anotar.Parameters.AddWithValue("@n", nombreScript);
            await anotar.ExecuteNonQueryAsync(cancelacion);
        }
    }

    /// <summary>Nombres de los scripts guardados dentro del programa, ordenados (000, 001, ...).</summary>
    public static List<string> ListarScripts() =>
        typeof(DatabaseMigrator).Assembly.GetManifestResourceNames()
            .Where(nombre => nombre.StartsWith("migraciones/") && nombre.EndsWith(".sql"))
            .Select(nombre => nombre["migraciones/".Length..])
            .OrderBy(nombre => nombre, StringComparer.Ordinal)
            .ToList();

    /// <summary>Lee el contenido de un script SQL guardado dentro del programa.</summary>
    private static string LeerScript(string nombre)
    {
        using var archivo = typeof(DatabaseMigrator).Assembly.GetManifestResourceStream("migraciones/" + nombre)
                            ?? throw new FileNotFoundException(nombre);
        using var lector = new StreamReader(archivo);
        return lector.ReadToEnd();
    }

    /// <summary>¿Este script ya se ejecutó antes? (se revisa en la tabla dbo.SchemaVersions).</summary>
    private static async Task<bool> YaFueAplicadoAsync(SqlConnection conexion, string nombreScript, CancellationToken cancelacion)
    {
        await using var consulta = new SqlCommand("SELECT COUNT(1) FROM dbo.SchemaVersions WHERE ScriptName = @n", conexion);
        consulta.Parameters.AddWithValue("@n", nombreScript);
        var cantidad = (int)(await consulta.ExecuteScalarAsync(cancelacion))!;
        return cantidad > 0;
    }

    /// <summary>Ejecuta un bloque de SQL.</summary>
    private static async Task EjecutarAsync(SqlConnection conexion, string sql, CancellationToken cancelacion)
    {
        await using var comando = new SqlCommand(sql, conexion) { CommandTimeout = 120 };
        await comando.ExecuteNonQueryAsync(cancelacion);
    }
}
