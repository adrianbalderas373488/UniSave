
// Materia: Verificación y Validación del Software
// Módulo: Cuentas
// Descripción: Reglas y contratos de UniSave.
// Fecha: 2026-11-07
// Trazabilidad: Precondición de sesión de CU-01 y CU-03

namespace UniSave.Dominio
{
    public sealed record Registro(string Correo, string Contrasena, bool EsEstudiante);
}
