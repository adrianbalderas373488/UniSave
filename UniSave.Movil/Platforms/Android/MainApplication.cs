// Archivo: MainApplication.cs
// Autor: Equipo UniSave (implementación asistida por Codex)
// Materia: Verificación y Validación del Software
// Módulo: Infraestructura
// Descripción: Configura la estructura de la aplicación.
// Fecha: 2026-11-09
// Trazabilidad: CU-01 a CU-07

using Android.App;
using Android.Runtime;

namespace UniSave.Movil;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
