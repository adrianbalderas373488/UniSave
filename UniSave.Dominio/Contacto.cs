
// Materia: Verificación y Validación del Software
// Módulo: Dominio
// Descripción: Reglas y contratos de UniSave.
// Fecha: 2026-11-07
// Trazabilidad: CU-01, CU-03, CU-06, CU-07

namespace UniSave.Dominio
{
    public sealed record Contacto(Guid Id, string Nombre, string Telefono);
}
