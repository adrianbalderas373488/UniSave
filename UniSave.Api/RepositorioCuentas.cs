
// Materia: Verificación y Validación del Software
// Módulo: Cuentas
// Descripción: Registro y acceso a cuentas.
// Fecha: 2026-11-07
// Trazabilidad: Precondición de sesión de CU-01 y CU-03

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
