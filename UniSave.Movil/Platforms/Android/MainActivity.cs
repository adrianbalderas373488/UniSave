// Archivo: MainActivity.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Infraestructura
// Descripción: Configura la estructura de la aplicación.
// Fecha: 2026-11-09
// Trazabilidad: CU-01 a CU-07

using Android.App;
using Android.Content.PM;
using Android.OS;

namespace UniSave.Movil;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
