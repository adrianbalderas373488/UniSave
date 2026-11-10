// Archivo: Correo.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Dominio
// Descripción: Valida y clasifica las direcciones de correo.
// Fecha: 2026-11-09
// Trazabilidad: Precondición de CU-01 y CU-03

using System.Net.Mail;

namespace UniSave.Dominio
{
    public static class Correo
    {
        public const string DominioInstitucional = "uabc.edu.mx";

        /// <summary>Normaliza un correo sin aceptar nombres de presentación ni espacios internos.</summary>
        public static string? Normalizar(string valor)
        {
            string limpio = valor.Trim();
            return MailAddress.TryCreate(limpio, out var correo) &&
                correo.Address.Equals(limpio, StringComparison.OrdinalIgnoreCase) &&
                !limpio.Any(char.IsWhiteSpace) && correo.Host.Contains('.')
                ? limpio.ToLowerInvariant()
                : null;
        }

        /// <summary>Clasifica el dominio exacto. Esta clasificación no verifica el buzón.</summary>
        public static bool EsInstitucional(string correo) =>
            Normalizar(correo)?.EndsWith("@" + DominioInstitucional,
                StringComparison.Ordinal) == true;
    }
}
