using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Fudis.Pagos.Api.Database;

/// <summary>Ayuda a reconocer errores específicos de SQL Server.</summary>
public static class SqlServerErrors
{
    // Números de error de SQL Server cuando se intenta guardar un registro que viola un índice único:
    // 2601 = fila duplicada en índice único, 2627 = clave primaria o restricción UNIQUE duplicada.
    private const int FilaDuplicada = 2601;
    private const int ClaveDuplicada = 2627;

    /// <summary>¿El error es "ya existe un registro con esos datos únicos"?</summary>
    public static bool EsRegistroDuplicado(DbUpdateException error)
    {
        if (error.InnerException is not SqlException errorSql)
            return false;

        foreach (SqlError detalle in errorSql.Errors)
        {
            if (detalle.Number is FilaDuplicada or ClaveDuplicada)
                return true;
        }

        return false;
    }
}
