using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Fudis.Pagos.Api.Configuration;

namespace Fudis.Pagos.Api.Services;

/// <summary>Resultado de revisar la firma. Solo "Valida" deja pasar el aviso.</summary>
public enum ResultadoFirma { Valida, Ausente, MalFormada, Vencida, Invalida }

/// <summary>
/// Comprueba que un aviso viene de verdad de la pasarela y que nadie lo modificó.
///
/// CÓMO FUNCIONA (como un sello secreto):
///   1. Fudis y la pasarela comparten una palabra secreta que nadie más conoce.
///   2. La pasarela "mezcla" el JSON con esa palabra (algoritmo HMAC-SHA256) y obtiene un código: la FIRMA.
///   3. Manda la firma en la cabecera:  X-Gateway-Signature: t=1730851200,v1=a3f9...
///        t  = momento en que firmó (segundos desde 1970)
///        v1 = la firma
///   4. Nosotros hacemos la misma mezcla con el JSON recibido. Si da el mismo código, es auténtico.
///      Si alguien cambió aunque sea una letra del JSON, o no conoce la palabra secreta, no coincide.
/// </summary>
public sealed class VerificacionFirmaService
{
    public const string NombreCabecera = "X-Gateway-Signature";

    private readonly FirmaOptions _opciones;
    private readonly TimeProvider _reloj;

    public VerificacionFirmaService(FirmaOptions opciones, TimeProvider reloj)
    {
        if (opciones.Secretos.Count == 0 || opciones.Secretos.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Falta configurar al menos un secreto en 'FirmaPasarela:Secretos'.");

        _opciones = opciones;
        _reloj = reloj;
    }

    /// <summary>Revisa la firma de un aviso en 4 pasos: ¿llegó?, ¿tiene buen formato?, ¿es reciente?, ¿coincide?</summary>
    public ResultadoFirma Verificar(string jsonRecibido, string? cabeceraFirma)
    {
        // 1. ¿Llegó la cabecera?
        if (string.IsNullOrWhiteSpace(cabeceraFirma))
            return ResultadoFirma.Ausente;

        // 2. ¿Tiene el formato "t=...,v1=..."?
        if (!IntentarLeerCabecera(cabeceraFirma, out long momentoFirma, out List<byte[]> firmasRecibidas))
            return ResultadoFirma.MalFormada;

        // 3. ¿Es reciente? (evita que alguien guarde un aviso real y lo reenvíe horas después)
        var antiguedad = _reloj.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(momentoFirma);
        if (antiguedad.Duration() > _opciones.Tolerancia)
            return ResultadoFirma.Vencida;

        // 4. ¿Coincide con alguno de nuestros secretos?
        foreach (var secreto in _opciones.Secretos)
        {
            byte[] firmaEsperada = CalcularFirma(jsonRecibido, secreto, momentoFirma);

            foreach (var firmaRecibida in firmasRecibidas)
            {
                // FixedTimeEquals compara siempre en el mismo tiempo. Una comparación normal se detiene en
                // la primera diferencia, y midiendo esos tiempos un atacante podría adivinar la firma.
                if (CryptographicOperations.FixedTimeEquals(firmaRecibida, firmaEsperada))
                    return ResultadoFirma.Valida;
            }
        }

        return ResultadoFirma.Invalida;
    }

    /// <summary>Genera la cabecera de firma. Lo usan las pruebas y el script que simula la pasarela.</summary>
    public static string GenerarCabecera(string json, string secreto, DateTimeOffset momento)
    {
        long segundos = momento.ToUnixTimeSeconds();
        byte[] firma = CalcularFirma(json, secreto, segundos);
        return $"t={segundos},v1={Convert.ToHexString(firma).ToLowerInvariant()}";
    }

    // Lo que se firma es: "<momento>.<json>". Incluir el momento impide cambiarlo sin romper la firma.
    private static byte[] CalcularFirma(string json, string secreto, long momento)
    {
        string textoAFirmar = momento.ToString(CultureInfo.InvariantCulture) + "." + json;
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), Encoding.UTF8.GetBytes(textoAFirmar));
    }

    // Lee "t=1730851200,v1=a3f9..." y separa el momento y la(s) firma(s).
    private static bool IntentarLeerCabecera(string cabecera, out long momento, out List<byte[]> firmas)
    {
        momento = 0;
        firmas = [];
        bool tieneMomento = false;

        foreach (var parte in cabecera.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var claveValor = parte.Split('=', 2);
            if (claveValor.Length != 2)
                return false;

            string clave = claveValor[0];
            string valor = claveValor[1];

            if (clave == "t")
            {
                tieneMomento = long.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out momento);
            }
            else if (clave == "v1")
            {
                try { firmas.Add(Convert.FromHexString(valor)); }
                catch (FormatException) { return false; }
            }
        }

        return tieneMomento && firmas.Count > 0;
    }
}
