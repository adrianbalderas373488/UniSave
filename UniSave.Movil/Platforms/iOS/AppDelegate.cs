// Archivo: AppDelegate.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Infraestructura
// Descripción: Configura la estructura de la aplicación.
// Fecha: 2026-11-09
// Trazabilidad: CU-01 a CU-07

using Foundation;

namespace UniSave.Movil;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
