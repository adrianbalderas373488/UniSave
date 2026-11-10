// Archivo: Contexto.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Dominio
// Descripción: Define el estado local y la regla de publicación.
// Fecha: 2026-11-09
// Trazabilidad: CU-01, CU-06, CU-07

namespace UniSave.Dominio
{
    public sealed record Contexto(Guid Id, DateTimeOffset Inicio, string? Destino);

    public sealed record EstadoLocal(
        DateTimeOffset? Consentimiento,
        Contexto? Alerta,
        Contexto? Trayecto,
        List<Contacto> Contactos)
    {
        public const int DuracionPulsacionSegundos = 2;

        /// <summary>Determina si existe consentimiento y un contexto habilitado para publicar.</summary>
        public bool PuedeCompartir(bool permiso) =>
            permiso && Consentimiento.HasValue && (Alerta is not null || Trayecto is not null);
    }
}
