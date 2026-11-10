
// Materia: Verificación y Validación del Software
// Módulo: Aplicación
// Descripción: Configura Blazor y el cliente de cuentas.
// Fecha: 2026-11-07
// Trazabilidad: CU-01 a CU-07.

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
