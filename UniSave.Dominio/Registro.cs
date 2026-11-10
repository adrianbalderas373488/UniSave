// Archivo: Registro.cs
// Autor: Equipo UniSave (implementación asistida por Codex)
// Materia: Verificación y Validación del Software
// Módulo: Dominio
// Descripción: Define los datos de registro e inicio de sesión.
// Fecha: 2026-11-09
// Trazabilidad: Precondición de CU-01 y CU-03

namespace UniSave.Dominio
{
    public sealed record Registro(string Correo, string Contrasena, bool EsEstudiante);
}
