// Archivo: Contacto.cs
// Autor: Equipo UniSave (implementación asistida por Codex)
// Materia: Verificación y Validación del Software
// Módulo: Dominio
// Descripción: Representa un contacto de confianza.
// Fecha: 2026-11-09
// Trazabilidad: RF-03, CU-03

namespace UniSave.Dominio
{
    public sealed record Contacto(Guid Id, string Nombre, string Telefono);
}
