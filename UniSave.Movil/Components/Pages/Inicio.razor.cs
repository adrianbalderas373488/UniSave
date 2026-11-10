// Archivo: Inicio.razor.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Aplicación móvil
// Descripción: Coordina las operaciones de la aplicación móvil.
// Fecha: 2026-11-09
// Trazabilidad: CU-01 a CU-07

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Web;
using UniSave.Dominio;
using PointerEventArgs = Microsoft.AspNetCore.Components.Web.PointerEventArgs;

namespace UniSave.Movil.Components.Pages
{
    public partial class Inicio : IDisposable
    {
        private EstadoLocal _estado = new(null, null, null, []);
        private string? _correoActivo;
        private string _correo = "";
        private string _clave = "";
        private string _destino = "";
        private string _nombreContacto = "";
        private string _telefonoContacto = "";
        private string _mensaje = "";
        private string _vista = "Inicio";
        private Guid? _contactoEditado;
        private Location? _ubicacion;
        private bool _crear;
        private bool _ocupado;
        private CancellationTokenSource? _pulsacion;

        private static string ClaveLocal(string correo) =>
            "estado-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(correo)));

        private Task AccederAsync() => EjecutarAsync(async () =>
        {
            string? correo = Correo.Normalizar(_correo);
            if (correo is null)
            {
                throw new InvalidOperationException("Escribe un correo electrónico válido.");
            }

            if (_crear && _clave.Length is < 12 or > 128)
            {
                throw new InvalidOperationException("Usa entre 12 y 128 caracteres.");
            }

            var registro = new Registro(correo, _clave, Correo.EsInstitucional(correo));
            await Cuentas.AccederAsync(registro, _crear);
            string? texto = await SecureStorage.Default.GetAsync(ClaveLocal(correo));
            _estado = texto is null ? new(null, null, null, []) :
                JsonSerializer.Deserialize<EstadoLocal>(texto) ?? throw new InvalidDataException();
            _correoActivo = correo;
            _clave = "";
            _mensaje = "Sesión iniciada.";
        });

        private async Task GuardarAsync(EstadoLocal estado)
        {
            await SecureStorage.Default.SetAsync(ClaveLocal(_correoActivo!),
                JsonSerializer.Serialize(estado));
            _estado = estado;
        }

        private Task CambiarConsentimientoAsync() => EjecutarAsync(async () =>
        {
            bool aceptar = !_estado.Consentimiento.HasValue;
            await GuardarAsync(_estado with
            {
                Consentimiento = aceptar ? DateTimeOffset.UtcNow : null
            });
            if (!aceptar)
            {
                _ubicacion = null;
            }

            _mensaje = aceptar ? "Consentimiento guardado." : "Consentimiento retirado.";
        });

        private Task PresionarAsync(PointerEventArgs evento) =>
            evento.IsPrimary && evento.Button == 0 ? MantenerAsync() : Task.CompletedTask;

        private Task PresionarTeclaAsync(KeyboardEventArgs evento) =>
            !evento.Repeat && (evento.Key is " " or "Enter") ? MantenerAsync() : Task.CompletedTask;

        private void SoltarTecla(KeyboardEventArgs evento)
        {
            if (evento.Key is " " or "Enter")
            {
                CancelarPulsacion();
            }
        }

        private async Task MantenerAsync()
        {
            if (_pulsacion is not null || _ocupado || _estado.Alerta is not null)
            {
                return;
            }

            using var pulsacion = new CancellationTokenSource();
            _pulsacion = pulsacion;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(EstadoLocal.DuracionPulsacionSegundos),
                    pulsacion.Token);
                pulsacion.Token.ThrowIfCancellationRequested();
                await EjecutarAsync(async () =>
                {
                    var alerta = new Contexto(Guid.NewGuid(), DateTimeOffset.UtcNow, null);
                    await GuardarAsync(_estado with { Alerta = alerta });
                    _mensaje = "Alerta activa. Puedes preparar un SMS para tus contactos.";
                });
            }
            catch (OperationCanceledException) when (pulsacion.IsCancellationRequested)
            {
                _mensaje = "Pulsación incompleta. No se activó la alerta.";
            }
            finally
            {
                _pulsacion = null;
            }
        }

        private void CancelarPulsacion() => _pulsacion?.Cancel();

        private Task IniciarTrayectoAsync() => EjecutarAsync(async () =>
        {
            string? destino = string.IsNullOrWhiteSpace(_destino) ? null : _destino.Trim();
            var trayecto = new Contexto(Guid.NewGuid(), DateTimeOffset.UtcNow, destino);
            await GuardarAsync(_estado with { Trayecto = trayecto });
            _mensaje = "Trayecto iniciado.";
        });

        private Task FinalizarTrayectoAsync() => EjecutarAsync(async () =>
        {
            await GuardarAsync(_estado with { Trayecto = null });
            _mensaje = "Trayecto finalizado.";
        });

        private Task FinalizarAlertaAsync() => EjecutarAsync(async () =>
        {
            await GuardarAsync(_estado with { Alerta = null });
            _mensaje = "Alerta finalizada.";
        });

        private Task GuardarContactoAsync() => EjecutarAsync(async () =>
        {
            string nombre = _nombreContacto.Trim();
            string telefono = _telefonoContacto.Trim();
            if (nombre.Length == 0 || telefono.Length is < 8 or > 16 ||
                !telefono.StartsWith('+') || telefono.Skip(1).Any(c => !char.IsAsciiDigit(c)))
            {
                throw new InvalidOperationException("Indica un nombre y un teléfono con prefijo +.");
            }

            var contacto = new Contacto(_contactoEditado ?? Guid.NewGuid(), nombre, telefono);
            var contactos = new List<Contacto>(_estado.Contactos);
            int indice = contactos.FindIndex(actual => actual.Id == contacto.Id);
            if (indice < 0)
            {
                contactos.Add(contacto);
            }
            else
            {
                contactos[indice] = contacto;
            }

            await GuardarAsync(_estado with { Contactos = contactos });
            LimpiarContacto();
            _mensaje = "Contacto guardado.";
        });

        private Task SubirAsync(Guid id) => EjecutarAsync(async () =>
        {
            var contactos = new List<Contacto>(_estado.Contactos);
            int indice = contactos.FindIndex(contacto => contacto.Id == id);
            if (indice > 0)
            {
                (contactos[indice], contactos[indice - 1]) =
                    (contactos[indice - 1], contactos[indice]);
                await GuardarAsync(_estado with { Contactos = contactos });
                _mensaje = "Prioridad actualizada.";
            }
        });

        private void Editar(Contacto contacto)
        {
            _contactoEditado = contacto.Id;
            _nombreContacto = contacto.Nombre;
            _telefonoContacto = contacto.Telefono;
        }

        private void LimpiarContacto()
        {
            _contactoEditado = null;
            _nombreContacto = "";
            _telefonoContacto = "";
        }

        private async Task<Location?> LeerUbicacionAsync(bool solicitarPermiso)
        {
            var permiso = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (solicitarPermiso && permiso != PermissionStatus.Granted)
            {
                permiso = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (permiso != PermissionStatus.Granted)
            {
                return null;
            }

            var solicitud = new GeolocationRequest(GeolocationAccuracy.Medium,
                TimeSpan.FromSeconds(5));
            try
            {
                return await Geolocation.Default.GetLocationAsync(solicitud);
            }
            catch (FeatureNotEnabledException)
            {
                return null;
            }
        }

        private Task ConsultarUbicacionAsync() => EjecutarAsync(async () =>
        {
            _ubicacion = await LeerUbicacionAsync(true);
            _mensaje = _ubicacion is null ? "Ubicación no disponible." : "Ubicación obtenida.";
        });

        private Task AbrirMapaAsync() => EjecutarAsync(async () =>
        {
            if (_ubicacion is not null)
            {
                await Map.Default.OpenAsync(_ubicacion);
            }
        });

        private Task PrepararSmsAsync(Contacto contacto) => EjecutarAsync(async () =>
        {
            if (!Sms.Default.IsComposeSupported)
            {
                throw new InvalidOperationException("El dispositivo no puede preparar SMS.");
            }

            bool permiso = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>()
                == PermissionStatus.Granted;
            Location? ubicacion = _estado.PuedeCompartir(permiso)
                ? await LeerUbicacionAsync(false) : null;
            string tipo = _estado.Alerta is not null ? "Alerta de emergencia" : "Trayecto activo";
            string posicion = ubicacion is null ? "Ubicación no incluida." :
                string.Format(CultureInfo.InvariantCulture,
                    "Ubicación: {0:F5}, {1:F5} ({2:HH:mm} UTC).",
                    ubicacion.Latitude, ubicacion.Longitude, ubicacion.Timestamp.UtcDateTime);
            await Sms.Default.ComposeAsync(new SmsMessage(
                $"UniSave · {tipo}. {posicion}", [contacto.Telefono]));
            _mensaje = "SMS preparado. Comprueba el envío en la app de mensajes.";
        });

        private void Salir()
        {
            _vista = "Inicio";
            CancelarPulsacion();
            _correoActivo = null;
            _estado = new(null, null, null, []);
            _ubicacion = null;
            _destino = "";
            LimpiarContacto();
            _mensaje = "Sesión cerrada.";
        }

        private async Task EjecutarAsync(Func<Task> operacion)
        {
            if (_ocupado)
            {
                return;
            }

            _ocupado = true;
            _mensaje = "";
            try
            {
                await operacion();
            }
            catch (InvalidOperationException error)
            {
                _mensaje = error.Message;
            }
            catch (Exception error) when (error is IOException or HttpRequestException or
                JsonException or TimeoutException or OperationCanceledException or
                FeatureNotSupportedException or PermissionException or UnauthorizedAccessException)
            {
                _mensaje = "No se pudo completar la operación. Revisa los permisos o la conexión.";
            }
            finally
            {
                _ocupado = false;
            }
        }

        public void Dispose() => CancelarPulsacion();
    }
}
