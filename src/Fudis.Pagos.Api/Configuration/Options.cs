namespace Fudis.Pagos.Api.Configuration;

// Estas clases se llenan con los valores de appsettings.json. Ver Configuration/DependencyInjection.cs.

/// <summary>Sección "Pagos" de appsettings.json.</summary>
public sealed class PagosOptions
{
    /// <summary>Nombre de la pasarela. Junto con el EventoId identifica un aviso de forma única.</summary>
    public string NombrePasarela { get; set; } = "pasarela-principal";

    /// <summary>Porcentaje de la tarifa de envío que gana el repartidor.</summary>
    public decimal PorcentajeRepartidor { get; set; } = 70m;
}

/// <summary>Sección "FirmaPasarela" de appsettings.json.</summary>
public sealed class FirmaOptions
{
    /// <summary>
    /// Palabras secretas compartidas con la pasarela. Se aceptan varias para poder cambiar el secreto
    /// sin cortar el servicio (se agrega el nuevo, la pasarela cambia, se quita el viejo).
    /// </summary>
    public List<string> Secretos { get; set; } = [];

    /// <summary>Antigüedad máxima de una firma. Evita que alguien reenvíe un aviso viejo.</summary>
    public TimeSpan Tolerancia { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Sección "EnvioNotificaciones" de appsettings.json.</summary>
public sealed class EnvioNotificacionesOptions
{
    /// <summary>Si es false, el trabajador en segundo plano no arranca (útil en pruebas).</summary>
    public bool Activado { get; set; } = true;

    /// <summary>Cuántos mensajes toma en cada vuelta.</summary>
    public int TamanoLote { get; set; } = 20;

    /// <summary>Cada cuánto revisa si hay mensajes nuevos.</summary>
    public TimeSpan EsperaEntreRevisiones { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Cuánto tiempo "aparta" un mensaje un trabajador. Si se cae, otro lo toma al vencer.</summary>
    public TimeSpan TiempoDeBloqueo { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Tiempo máximo que se espera al restaurante en cada envío. DEBE ser menor que TiempoDeBloqueo:
    /// si no, el bloqueo vencería mientras todavía se está enviando y otro trabajador mandaría el aviso otra vez.
    /// </summary>
    public TimeSpan TiempoMaximoPorEnvio { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Después de estos intentos fallidos, se descarta el mensaje y queda para revisión manual.</summary>
    public int MaximoIntentos { get; set; } = 10;

    /// <summary>Espera después del primer fallo. Se duplica en cada fallo: 5s, 10s, 20s, 40s...</summary>
    public TimeSpan EsperaInicial { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Espera máxima entre intentos.</summary>
    public TimeSpan EsperaMaxima { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Revisa que la configuración tenga sentido. Se llama al arrancar: si está mal, la API no inicia.</summary>
    public void Validar()
    {
        if (TamanoLote < 1)
            throw new InvalidOperationException("EnvioNotificaciones:TamanoLote debe ser al menos 1.");
        if (MaximoIntentos < 1)
            throw new InvalidOperationException("EnvioNotificaciones:MaximoIntentos debe ser al menos 1.");
        if (TiempoMaximoPorEnvio >= TiempoDeBloqueo)
            throw new InvalidOperationException("EnvioNotificaciones:TiempoMaximoPorEnvio debe ser menor que TiempoDeBloqueo.");
    }

    /// <summary>Calcula cuánto esperar antes del siguiente intento: se duplica cada vez, con un tope.</summary>
    public TimeSpan CalcularEspera(int intentosHechos)
    {
        var duplicaciones = Math.Clamp(intentosHechos - 1, 0, 30);
        var espera = EsperaInicial.Ticks * Math.Pow(2, duplicaciones);

        if (espera >= EsperaMaxima.Ticks)
            return EsperaMaxima;

        return TimeSpan.FromTicks((long)espera);
    }
}
