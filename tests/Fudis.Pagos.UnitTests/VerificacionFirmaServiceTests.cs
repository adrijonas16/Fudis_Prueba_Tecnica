using Fudis.Pagos.Api.Configuration;
using Fudis.Pagos.Api.Services;
using Microsoft.Extensions.Time.Testing;

namespace Fudis.Pagos.UnitTests;

/// <summary>
/// Pruebas de la firma HMAC: que acepte solo avisos auténticos, recientes y sin modificar.
/// Usan un reloj falso (FakeTimeProvider) para controlar la hora.
/// </summary>
public class VerificacionFirmaServiceTests
{
    private const string Json = """{"eventId":"evt_1","orderId":1}""";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 20, 0, 0, TimeSpan.Zero);

    private static VerificacionFirmaService CrearVerificador(params string[] secretos)
    {
        var opciones = new FirmaOptions { Secretos = [.. secretos], Tolerancia = TimeSpan.FromMinutes(5) };
        return new VerificacionFirmaService(opciones, new FakeTimeProvider(Ahora));
    }

    [Fact]
    public void Acepta_una_firma_correcta()
    {
        var cabecera = VerificacionFirmaService.GenerarCabecera(Json, "secreto", Ahora);
        Assert.Equal(ResultadoFirma.Valida, CrearVerificador("secreto").Verificar(Json, cabecera));
    }

    [Fact]
    public void Rechaza_si_alguien_modifico_el_json()
    {
        var cabecera = VerificacionFirmaService.GenerarCabecera(Json, "secreto", Ahora);
        Assert.Equal(ResultadoFirma.Invalida, CrearVerificador("secreto").Verificar(Json.Replace("1}", "2}"), cabecera));
    }

    [Fact]
    public void Rechaza_una_firma_hecha_con_otro_secreto()
    {
        var cabecera = VerificacionFirmaService.GenerarCabecera(Json, "secreto-del-atacante", Ahora);
        Assert.Equal(ResultadoFirma.Invalida, CrearVerificador("secreto").Verificar(Json, cabecera));
    }

    [Fact]
    public void Rechaza_una_firma_vieja_para_evitar_reenvios()
    {
        var cabecera = VerificacionFirmaService.GenerarCabecera(Json, "secreto", Ahora.AddMinutes(-6));
        Assert.Equal(ResultadoFirma.Vencida, CrearVerificador("secreto").Verificar(Json, cabecera));
    }

    [Fact]
    public void Rechaza_una_firma_del_futuro()
    {
        var cabecera = VerificacionFirmaService.GenerarCabecera(Json, "secreto", Ahora.AddMinutes(6));
        Assert.Equal(ResultadoFirma.Vencida, CrearVerificador("secreto").Verificar(Json, cabecera));
    }

    [Fact]
    public void Acepta_cualquiera_de_los_secretos_mientras_se_cambia_el_secreto()
    {
        var cabecera = VerificacionFirmaService.GenerarCabecera(Json, "secreto-nuevo", Ahora);
        Assert.Equal(ResultadoFirma.Valida, CrearVerificador("secreto-viejo", "secreto-nuevo").Verificar(Json, cabecera));
    }

    [Theory]
    [InlineData(null, ResultadoFirma.Ausente)]
    [InlineData("", ResultadoFirma.Ausente)]
    [InlineData("basura", ResultadoFirma.MalFormada)]
    [InlineData("t=abc,v1=00", ResultadoFirma.MalFormada)]
    [InlineData("t=1793908800", ResultadoFirma.MalFormada)]
    [InlineData("t=1793908800,v1=zz", ResultadoFirma.MalFormada)]
    public void Rechaza_cabecera_ausente_o_mal_escrita(string? cabecera, ResultadoFirma esperado)
    {
        Assert.Equal(esperado, CrearVerificador("secreto").Verificar(Json, cabecera));
    }

    [Fact]
    public void No_arranca_sin_secretos_configurados()
    {
        Assert.Throws<InvalidOperationException>(() => CrearVerificador());
    }
}
