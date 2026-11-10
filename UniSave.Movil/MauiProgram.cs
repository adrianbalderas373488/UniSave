// Archivo: MauiProgram.cs
// Autor: Equipo UniSave (implementación asistida por Codex)
// Materia: Verificación y Validación del Software
// Módulo: Infraestructura
// Descripción: Configura la estructura de la aplicación.
// Fecha: 2026-11-09
// Trazabilidad: CU-01 a CU-07

namespace UniSave.Movil
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var constructor = MauiApp.CreateBuilder().UseMauiApp<App>();
            constructor.Services.AddMauiBlazorWebView();
            constructor.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
            constructor.Services.AddSingleton<CuentaCliente>();
            return constructor.Build();
        }
    }
}
