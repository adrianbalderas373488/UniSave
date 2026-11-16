// Archivo: Program.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: API
// Descripción: Configura el servidor y los puntos de acceso a cuentas.
// Fecha: 2026-11-09
// Trazabilidad: CU-01, CU-03

using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using UniSave.Dominio;
using UniSave.Api;
using UniSave.Api.Components;

var constructor = WebApplication.CreateBuilder(args);
constructor.Services.AddAuthentication("Sesion").AddBearerToken("Sesion");
constructor.Services.AddAuthorization();
constructor.Services.AddSingleton<RepositorioCuentas>();
constructor.Services.AddSingleton<PasswordHasher<Cuenta>>();
constructor.Services.AddSingleton<Seguimiento>();
constructor.Services.AddHttpClient<ReceptorAutoridades>(cliente =>
    cliente.Timeout = TimeSpan.FromSeconds(5));
constructor.Services.AddRazorComponents().AddInteractiveServerComponents();
constructor.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = 429;
    // Un contador por dirección IP: una persona no agota el límite de todas las demás.
    // Detrás de un proxy, configurar ForwardedHeaders para ver la IP real.
    opciones.AddPolicy("cuentas", contexto => RateLimitPartition.GetFixedWindowLimiter(
        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});
var aplicacion = constructor.Build();
aplicacion.UseRateLimiter();
aplicacion.UseAuthentication();
aplicacion.UseAuthorization();
aplicacion.UseStaticFiles();
aplicacion.UseAntiforgery();
aplicacion.MapStaticAssets();
aplicacion.MapRazorComponents<Aplicacion>().AddInteractiveServerRenderMode();

aplicacion.MapGet("/salud", () => Results.Ok());
var cuentas = aplicacion.MapGroup("/cuentas").RequireRateLimiting("cuentas");
cuentas.MapPost("/registro", async (Registro entrada, RepositorioCuentas repositorio,
    PasswordHasher<Cuenta> cifrador, CancellationToken cancelacion) =>
{
    string? correo = Correo.Normalizar(entrada.Correo ?? "");
    if (correo is null || entrada.Contrasena is null ||
        entrada.Contrasena.Length is < 12 or > 128 ||
        entrada.EsEstudiante != Correo.EsInstitucional(correo))
    {
        return Results.BadRequest();
    }

    var cuenta = new Cuenta(correo, "", entrada.EsEstudiante);
    cuenta = cuenta with { Resumen = cifrador.HashPassword(cuenta, entrada.Contrasena) };
    return await repositorio.AgregarAsync(cuenta, cancelacion)
        ? Results.Created("/cuentas/perfil", new { cuenta.EsEstudiante })
        : Results.Conflict();
});
cuentas.MapPost("/sesion", async (Registro entrada, RepositorioCuentas repositorio,
    CancellationToken cancelacion) =>
{
    string? correo = Correo.Normalizar(entrada.Correo ?? "");
    if (correo is null || entrada.Contrasena is null || entrada.Contrasena.Length > 128)
    {
        return Results.Unauthorized();
    }

    var (resultado, cuenta) = await repositorio.VerificarAsync(correo, entrada.Contrasena,
        cancelacion);
    if (cuenta is null)
    {
        return resultado == ResultadoAcceso.Bloqueado
            ? Results.StatusCode(StatusCodes.Status429TooManyRequests)
            : Results.Unauthorized();
    }

    var identidad = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, cuenta.Correo)], "Sesion");
    return Results.SignIn(new ClaimsPrincipal(identidad), authenticationScheme: "Sesion");
});
cuentas.MapGet("/perfil", (ClaimsPrincipal sesion) =>
{
    string correo = sesion.Identity!.Name!;
    return Results.Ok(new { Correo = correo, EsEstudiante = Correo.EsInstitucional(correo) });
}).RequireAuthorization();

aplicacion.Run();
