using Fudis.Pagos.Api.Database;
using Fudis.Pagos.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
// /health revisa SQL Server. Máximo 5 s: un balanceador no espera más que eso.
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("base-de-datos", timeout: TimeSpan.FromSeconds(5));
builder.Services.AddPagosServices(builder.Configuration); // ver Configuration/DependencyInjection.cs

var app = builder.Build();

// Solo en desarrollo: crea la base y aplica los scripts de db/migrations al arrancar.
// En producción esto lo hace el pipeline de despliegue o el DBA, no la aplicación.
if (app.Configuration.GetValue<bool>("BaseDeDatos:AplicarMigracionesAlIniciar"))
{
    var migrador = app.Services.GetRequiredService<DatabaseMigrator>();
    var cadenaConexion = app.Configuration.GetConnectionString("Fudis")!;
    await migrador.CrearBaseSiNoExisteAsync(cadenaConexion);
    await migrador.AplicarMigracionesAsync(cadenaConexion);
}

app.UseExceptionHandler();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Necesario para que las pruebas de integración puedan levantar la API.
public partial class Program;
