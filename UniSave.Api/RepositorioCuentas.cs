// Archivo: RepositorioCuentas.cs
// Autor: Equipo UniSave (implementación asistida por Codex)
// Materia: Verificación y Validación del Software
// Módulo: Cuentas
// Descripción: Persiste y consulta las cuentas de UniSave.
// Fecha: 2026-11-09
// Trazabilidad: Precondición de CU-01 y CU-03

using System.Text.Json;

namespace UniSave.Api
{
    public sealed record Cuenta(string Correo, string Resumen, bool EsEstudiante);

    public sealed class RepositorioCuentas(IConfiguration configuracion, IWebHostEnvironment entorno)
    {
        private readonly SemaphoreSlim _acceso = new(1);
        private readonly string _archivo = Path.GetFullPath(
            configuracion["UniSave:ArchivoCuentas"] ?? "datos/cuentas.json",
            entorno.ContentRootPath);

        /// <summary>Busca una cuenta por correo normalizado.</summary>
        /// <param name="correo">Correo normalizado de la cuenta.</param>
        /// <param name="cancelacion">Señal para cancelar el acceso al archivo.</param>
        /// <returns>La cuenta registrada o un valor nulo si no existe.</returns>
        /// <exception cref="IOException">No se pudo leer el archivo de cuentas.</exception>
        public async Task<Cuenta?> BuscarAsync(string correo, CancellationToken cancelacion)
        {
            await _acceso.WaitAsync(cancelacion);
            try
            {
                return (await LeerAsync(cancelacion)).Find(cuenta => cuenta.Correo == correo);
            }
            finally
            {
                _acceso.Release();
            }
        }

        /// <summary>Guarda una cuenta nueva y devuelve falso si ya existe.</summary>
        /// <param name="cuenta">Cuenta validada con su contraseña resumida.</param>
        /// <param name="cancelacion">Señal para cancelar la operación.</param>
        /// <returns>Verdadero si se guardó la cuenta.</returns>
        /// <exception cref="IOException">No se pudo actualizar el archivo de cuentas.</exception>
        public async Task<bool> AgregarAsync(Cuenta cuenta, CancellationToken cancelacion)
        {
            await _acceso.WaitAsync(cancelacion);
            try
            {
                var cuentas = await LeerAsync(cancelacion);
                if (cuentas.Exists(actual => actual.Correo == cuenta.Correo))
                {
                    return false;
                }

                cuentas.Add(cuenta);
                Directory.CreateDirectory(Path.GetDirectoryName(_archivo)!);
                string temporal = _archivo + ".tmp";
                await File.WriteAllTextAsync(temporal, JsonSerializer.Serialize(cuentas),
                    cancelacion);
                File.Move(temporal, _archivo, true);
                return true;
            }
            finally
            {
                _acceso.Release();
            }
        }

        private async Task<List<Cuenta>> LeerAsync(CancellationToken cancelacion)
        {
            if (!File.Exists(_archivo))
            {
                return [];
            }

            await using var flujo = File.OpenRead(_archivo);
            return await JsonSerializer.DeserializeAsync<List<Cuenta>>(flujo,
                cancellationToken: cancelacion) ?? throw new InvalidDataException();
        }
    }
}
