// Archivo: RepositorioCuentas.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Cuentas
// Descripción: Persiste, consulta y verifica las cuentas de UniSave.
// Fecha: 2026-11-15
// Trazabilidad: Precondición de CU-01 y CU-03

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;

namespace UniSave.Api
{
    public sealed record Cuenta(string Correo, string Resumen, bool EsEstudiante);

    public enum ResultadoAcceso
    {
        Correcto,
        Incorrecto,
        Bloqueado
    }

    public sealed class RepositorioCuentas(IConfiguration configuracion, IWebHostEnvironment entorno,
        PasswordHasher<Cuenta> cifrador)
    {
        private const int MaximoFallos = 5;
        private static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(15);

        // shortcut: memoria de un solo servidor; con varios servidores debe ir a un almacén
        // compartido. Solo cuenta correos registrados, así que no crece más que las cuentas.
        private readonly ConcurrentDictionary<string, (int Fallos, DateTimeOffset Desde)> _fallos = new();
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

        /// <summary>
        /// Verifica la contraseña. Tras 5 fallos en 15 minutos bloquea el correo, tanto en el
        /// portal como en la API, porque ambos llaman aquí.
        /// </summary>
        /// <param name="correo">Correo normalizado.</param>
        /// <param name="clave">Contraseña escrita por la persona.</param>
        /// <param name="cancelacion">Señal para cancelar la lectura.</param>
        /// <returns>El resultado y la cuenta cuando el acceso es correcto.</returns>
        /// <exception cref="IOException">No se pudo leer el archivo de cuentas.</exception>
        public async Task<(ResultadoAcceso Resultado, Cuenta? Cuenta)> VerificarAsync(string correo,
            string clave, CancellationToken cancelacion)
        {
            DateTimeOffset ahora = DateTimeOffset.UtcNow;
            if (_fallos.TryGetValue(correo, out var previo) && ahora - previo.Desde < Bloqueo &&
                previo.Fallos >= MaximoFallos)
            {
                return (ResultadoAcceso.Bloqueado, null);
            }

            var cuenta = await BuscarAsync(correo, cancelacion);
            if (cuenta is null)
            {
                return (ResultadoAcceso.Incorrecto, null);
            }

            if (cifrador.VerifyHashedPassword(cuenta, cuenta.Resumen, clave) ==
                PasswordVerificationResult.Failed)
            {
                _fallos.AddOrUpdate(correo, _ => (1, ahora), (_, actual) =>
                    ahora - actual.Desde < Bloqueo ? (actual.Fallos + 1, actual.Desde) : (1, ahora));
                return (ResultadoAcceso.Incorrecto, null);
            }

            _fallos.TryRemove(correo, out _);
            return (ResultadoAcceso.Correcto, cuenta);
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
