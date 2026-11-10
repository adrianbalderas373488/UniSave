// Archivo: CuentaCliente.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Cuentas
// Descripción: Conecta el registro y acceso móvil con la API.
// Fecha: 2026-11-09
// Trazabilidad: Precondición de CU-01 y CU-03

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using UniSave.Dominio;

namespace UniSave.Movil
{
    public sealed class CuentaCliente(HttpClient cliente)
    {
        private static readonly Uri? _servidor = LeerServidor();

        /// <summary>Registra una cuenta y comprueba sus credenciales contra la API.</summary>
        /// <param name="registro">Correo, contraseña y clasificación de la cuenta.</param>
        /// <param name="crear">Indica si se solicita el alta antes del acceso.</param>
        /// <returns>Token de la sesión iniciada.</returns>
        /// <exception cref="InvalidOperationException">Acceso inválido o sin configurar.</exception>
        /// <exception cref="HttpRequestException">La API rechazó o no recibió la petición.</exception>
        public async Task<string> AccederAsync(Registro registro, bool crear)
        {
            Uri servidor = _servidor ?? throw new InvalidOperationException(
                "Esta versión de UniSave necesita configurarse antes de poder iniciar sesión.");

            if (crear)
            {
                using var alta = await cliente.PostAsJsonAsync(
                    new Uri(servidor, "cuentas/registro"), registro);
                if (alta.StatusCode == HttpStatusCode.Conflict)
                {
                    throw new InvalidOperationException("Ya hay una cuenta con ese correo.");
                }

                alta.EnsureSuccessStatusCode();
            }

            using var acceso = await cliente.PostAsJsonAsync(
                new Uri(servidor, "cuentas/sesion"), registro);
            if (acceso.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("El correo o la contraseña son incorrectos.");
            }

            acceso.EnsureSuccessStatusCode();
            var sesion = await acceso.Content.ReadFromJsonAsync<Sesion>();
            return sesion?.AccessToken is { Length: > 0 } token
                ? token
                : throw new InvalidOperationException("El servidor no devolvió una sesión.");
        }

        private static Uri? LeerServidor()
        {
            string? direccion = typeof(CuentaCliente).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(atributo => atributo.Key == "UniSaveServidor")?.Value;
            if (!Uri.TryCreate(direccion, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 ||
                uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            {
                return null;
            }

            return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        }

        private sealed record Sesion(string AccessToken);
    }
}
