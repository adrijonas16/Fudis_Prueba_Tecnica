using Fudis.Pagos.Api.Filters;
using Microsoft.AspNetCore.Mvc;

namespace Fudis.Pagos.UnitTests;

/// <summary>
/// Pruebas del filtro [ValidarFirmaPasarela]. Que rechace firmas inválidas con 401 se prueba de punta a punta
/// en PagosControllerTests; aquí se protege el ORDEN en que corre respecto de los otros filtros.
/// </summary>
public class ValidarFirmaPasarelaAttributeTests
{
    [Fact]
    public void Corre_despues_del_limite_de_tamano_para_no_leer_cuerpos_gigantes()
    {
        var filtroFirma = new ValidarFirmaPasarelaAttribute();
        var limiteTamano = new RequestSizeLimitAttribute(64 * 1024);

        // En ASP.NET, a menor Order antes se ejecuta. El límite debe aplicarse antes de leer el cuerpo.
        Assert.True(filtroFirma.Order > limiteTamano.Order,
            $"El filtro de firma (Order {filtroFirma.Order}) debe correr después de RequestSizeLimit (Order {limiteTamano.Order}).");
    }
}
