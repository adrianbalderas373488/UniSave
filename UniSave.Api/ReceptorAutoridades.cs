// Archivo: ReceptorAutoridades.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Autoridades
// Descripción: Envía el reporte de una alerta al receptor de autoridades configurado.
// Fecha: 2026-11-15
// Trazabilidad: RF-01, RIE-01, CU-01

using System.Net.Http.Json;

namespace UniSave.Api
{
    public enum ResultadoReporte
    {
        NoConfigurado,
        Recibido,
        Fallido
    }

    /// <summary>
    /// Entrega el reporte a la dirección de UniSave:ReceptorAutoridades. En pruebas debe ser un
    /// receptor acordado con el cliente; nunca un número o servicio de emergencias real.
    /// </summary>
    public sealed class ReceptorAutoridades(HttpClient http, IConfiguration configuracion)
    {
        public bool Configurado => Destino is not null;

        private Uri? Destino =>
            Uri.TryCreate(configuracion["UniSave:ReceptorAutoridades"], UriKind.Absolute,
                out var destino) && destino.Scheme is "https" or "http" ? destino : null;

        /// <summary>Envía el reporte y devuelve el resultado real de la entrega.</summary>
        /// <param name="alerta">Identificador de la alerta.</param>
        /// <param name="estudiante">Correo del titular.</param>
        /// <param name="posicion">Última posición autorizada o nula si no hay consentimiento.</param>
        /// <param name="cancelacion">Cancela el envío al cerrar la vista.</param>
        public async Task<ResultadoReporte> EnviarAsync(Guid alerta, string estudiante,
            Posicion? posicion, CancellationToken cancelacion)
        {
            if (Destino is not { } destino)
            {
                return ResultadoReporte.NoConfigurado;
            }

            try
            {
                using var respuesta = await http.PostAsJsonAsync(destino, new
                {
                    Tipo = "ALERTA",
                    Alerta = alerta,
                    Estudiante = estudiante,
                    Posicion = posicion,
                    Fecha = DateTimeOffset.UtcNow
                }, cancelacion);
                return respuesta.IsSuccessStatusCode
                    ? ResultadoReporte.Recibido : ResultadoReporte.Fallido;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                return ResultadoReporte.Fallido;
            }
        }
    }
}
