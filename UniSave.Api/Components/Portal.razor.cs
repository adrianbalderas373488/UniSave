// Archivo: Portal.razor.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Portal
// Descripción: Coordina la sesión, invitaciones y el seguimiento.
// Fecha: 2026-11-09
// Trazabilidad: CU-01 a CU-07

using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.JSInterop;
using UniSave.Dominio;

namespace UniSave.Api.Components
{
    public partial class Portal : IAsyncDisposable
    {
        private readonly CancellationTokenSource _vida = new();
        private readonly Guid _sesion = Guid.NewGuid();
        private Cuenta? _cuenta;
        private CancellationTokenSource? _pulsacion;
        private Task? _actualizaciones;
        private string _correo = "";
        private string _clave = "";
        private string _familiar = "";
        private string _destino = "";
        private string? _destinoActivo;
        private string _mensaje = "";
        private string _vista = "Inicio";
        private bool _crear;
        private bool _ocupado;
        private bool _trayecto;
        private bool _alerta;
        private Guid? _trayectoId;
        private Guid? _alertaId;
        private DateTimeOffset? _inicioTrayecto;
        private int _generacion;

        [Inject] private RepositorioCuentas Cuentas { get; set; } = null!;
        [Inject] private PasswordHasher<Cuenta> Cifrador { get; set; } = null!;
        [Inject] private Seguimiento Seguimiento { get; set; } = null!;
        [Inject] private IJSRuntime Navegador { get; set; } = null!;

        private IReadOnlyList<Invitacion> Invitaciones => _cuenta is null ? [] :
            Seguimiento.ConsultarInvitaciones(_cuenta.Correo);

        private IReadOnlyList<Aviso> Avisos => _cuenta is null ? [] :
            Seguimiento.ConsultarAvisos(_cuenta.Correo);

        private IReadOnlyList<TrayectoCompartido> Historial => _cuenta is null ? [] :
            Seguimiento.ConsultarHistorial(_cuenta.Correo);

        private IReadOnlyList<Notificacion> Notificaciones => _cuenta is null ? [] :
            Seguimiento.ConsultarNotificaciones(_cuenta.Correo);

        private int SinLeer => Notificaciones.Count(aviso => !aviso.Leida);

        private bool Consiente => _cuenta is { EsEstudiante: true } cuenta &&
            Seguimiento.ConsultarConsentimiento(cuenta.Correo).HasValue;

        protected override void OnInitialized()
        {
            _actualizaciones = ActualizarAsync(_vida.Token);
        }

        private async Task AccederAsync()
        {
            if (_ocupado)
            {
                return;
            }

            _ocupado = true;
            _mensaje = "";
            try
            {
                string? correo = Correo.Normalizar(_correo);
                if (correo is null || _clave.Length is < 12 or > 128)
                {
                    _mensaje = "Revisa el correo y la contraseña (12 a 128 caracteres).";
                    return;
                }

                if (_crear)
                {
                    var nueva = new Cuenta(correo, "", Correo.EsInstitucional(correo));
                    nueva = nueva with { Resumen = Cifrador.HashPassword(nueva, _clave) };
                    if (!await Cuentas.AgregarAsync(nueva, _vida.Token))
                    {
                        _mensaje = "Ya hay una cuenta con ese correo.";
                        return;
                    }

                    _cuenta = nueva;
                }
                else
                {
                    var cuenta = await Cuentas.BuscarAsync(correo, _vida.Token);
                    if (cuenta is null || Cifrador.VerifyHashedPassword(cuenta, cuenta.Resumen,
                        _clave) == PasswordVerificationResult.Failed)
                    {
                        _mensaje = "El correo o la contraseña son incorrectos.";
                        return;
                    }

                    _cuenta = cuenta;
                }

                _mensaje = "Sesión iniciada.";
            }
            catch (IOException)
            {
                _mensaje = "El servidor no pudo acceder a los datos de la cuenta.";
            }
            finally
            {
                _clave = "";
                _ocupado = false;
            }
        }

        private async Task InvitarAsync()
        {
            if (_cuenta is not { EsEstudiante: true } estudiante)
            {
                return;
            }

            string? correo = Correo.Normalizar(_familiar);
            if (correo is null || Correo.EsInstitucional(correo))
            {
                _mensaje = "Indica el correo personal del familiar.";
                return;
            }

            try
            {
                var familiar = await Cuentas.BuscarAsync(correo, _vida.Token);
                if (_cuenta != estudiante)
                {
                    return;
                }

                _mensaje = familiar is null ? "El familiar debe registrarse primero." :
                    Seguimiento.Invitar(estudiante.Correo, correo)
                    ? "Invitación disponible en la cuenta del familiar."
                    : "Ya existe una invitación para ese familiar.";
                _familiar = "";
            }
            catch (IOException)
            {
                _mensaje = "No se pudo consultar la cuenta del familiar.";
            }
        }

        private void Responder(Invitacion invitacion, bool aceptar)
        {
            if (_cuenta is { EsEstudiante: false } familiar)
            {
                GuardarCambio(() =>
                    Seguimiento.Responder(familiar.Correo, invitacion.Estudiante, aceptar),
                    aceptar ? "Invitación aceptada." : "Invitación rechazada.");
            }
        }

        private void Desvincular(Invitacion invitacion)
        {
            if (_cuenta is { } cuenta)
            {
                GuardarCambio(() => Seguimiento.Desvincular(cuenta.Correo, invitacion),
                    "Vínculo retirado.");
            }
        }

        private void CambiarConsentimiento()
        {
            if (_cuenta is not { EsEstudiante: true } cuenta)
            {
                return;
            }

            bool permitir = !Consiente;
            _generacion++;
            GuardarCambio(() => Seguimiento.EstablecerConsentimiento(cuenta.Correo, permitir),
                permitir ? "Consentimiento guardado en tu perfil." : "Publicación detenida.");
        }

        private async Task IniciarTrayectoAsync()
        {
            if (_trayecto || _cuenta is not { EsEstudiante: true })
            {
                return;
            }

            _trayecto = true;
            _trayectoId = Guid.NewGuid();
            _inicioTrayecto = DateTimeOffset.UtcNow;
            _destinoActivo = _destino.Trim();
            _mensaje = "Trayecto iniciado.";
            await PublicarAsync(true);
        }

        private void FinalizarTrayecto()
        {
            _trayecto = false;
            _destinoActivo = null;
            ActualizarContexto("Trayecto finalizado.");
            CerrarHistorial(Consiente);
            _trayectoId = null;
            _inicioTrayecto = null;
        }

        private void FinalizarAlerta()
        {
            _alerta = false;
            _alertaId = null;
            ActualizarContexto("Alerta finalizada.");
        }

        private void ActualizarContexto(string mensaje)
        {
            _generacion++;
            if (_cuenta is { } cuenta)
            {
                Seguimiento.Detener(cuenta.Correo, _sesion);
            }

            _mensaje = mensaje;
        }

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
            if (_pulsacion is not null || _alerta)
            {
                return;
            }

            using var pulsacion = CancellationTokenSource.CreateLinkedTokenSource(_vida.Token);
            _pulsacion = pulsacion;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(EstadoLocal.DuracionPulsacionSegundos),
                    pulsacion.Token);
                pulsacion.Token.ThrowIfCancellationRequested();
                _alerta = true;
                _alertaId = Guid.NewGuid();
                _mensaje = "Alerta activa.";
                await PublicarAsync(true);
            }
            catch (OperationCanceledException) when (pulsacion.IsCancellationRequested)
            {
                _mensaje = "Pulsación incompleta.";
            }
            finally
            {
                _pulsacion = null;
            }
        }

        private void CancelarPulsacion() => _pulsacion?.Cancel();

        private async Task PublicarAsync(bool solicitarPermiso)
        {
            if ((!_trayecto && !_alerta) || _cuenta is not { EsEstudiante: true } estudiante ||
                Seguimiento.ConsultarConsentimiento(estudiante.Correo) is not { } consentimiento)
            {
                return;
            }

            int generacion = _generacion;
            var aviso = new Aviso(estudiante.Correo, _alerta, _trayecto, _destinoActivo,
                null, DateTimeOffset.UtcNow, _trayectoId, _inicioTrayecto, _alertaId);
            try
            {
                if (solicitarPermiso && !Seguimiento.Publicar(_sesion, consentimiento, aviso))
                {
                    return;
                }

                var posicion = await Navegador.InvokeAsync<Posicion?>("unisave.posicion",
                    _vida.Token, solicitarPermiso);
                if (generacion != _generacion || estudiante != _cuenta)
                {
                    return;
                }

                bool publicado = Seguimiento.Publicar(_sesion, consentimiento, aviso with
                {
                    Posicion = posicion,
                    Actualizado = DateTimeOffset.UtcNow
                });
                if (solicitarPermiso && publicado)
                {
                    _mensaje = posicion is null ? "Actividad compartida sin ubicación." :
                        "Actividad y ubicación compartidas con tus familiares vinculados.";
                }
            }
            catch (JSException)
            {
                if (solicitarPermiso)
                {
                    _mensaje = "No se pudo obtener la ubicación. Revisa el permiso de Safari.";
                }
            }
            catch (IOException)
            {
                _mensaje = "No se pudo compartir la actividad. Inténtalo de nuevo.";
            }
        }

        private async Task ActualizarAsync(CancellationToken cancelacion)
        {
            using var reloj = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try
            {
                while (await reloj.WaitForNextTickAsync(cancelacion))
                {
                    await InvokeAsync(async () =>
                    {
                        if (_cuenta is not null)
                        {
                            await PublicarAsync(false);
                            StateHasChanged();
                        }
                    });
                }
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
            {
                // La vista se cerró.
            }
            catch (JSDisconnectedException)
            {
                if (_cuenta is { } cuenta)
                {
                    Seguimiento.Detener(cuenta.Correo, _sesion);
                }
            }
        }

        private static string Mapa(Posicion posicion) => string.Format(
            CultureInfo.InvariantCulture,
            "https://www.openstreetmap.org/?mlat={0:F5}&mlon={1:F5}#map=16/{0:F5}/{1:F5}",
            posicion.Latitud, posicion.Longitud);

        private void MarcarNotificaciones()
        {
            if (_cuenta is { } cuenta)
            {
                GuardarCambio(() => Seguimiento.MarcarLeidas(cuenta.Correo), "");
            }
        }

        private void CerrarHistorial(bool llegada)
        {
            if (_cuenta is { } cuenta && _trayectoId is { } id)
            {
                GuardarCambio(() => Seguimiento.CerrarTrayecto(cuenta.Correo, id, llegada),
                    _mensaje);
            }
        }

        private void GuardarCambio(Action cambio, string mensaje)
        {
            try
            {
                cambio();
                _mensaje = mensaje;
            }
            catch (IOException)
            {
                _mensaje = "No se pudo guardar el cambio.";
            }
        }

        private static string Titulo(TipoNotificacion tipo) => tipo switch
        {
            TipoNotificacion.Invitacion => "Nueva invitación",
            TipoNotificacion.Aceptacion => "Invitación aceptada",
            TipoNotificacion.Inicio => "Viaje compartido",
            TipoNotificacion.Llegada => "Llegada confirmada",
            TipoNotificacion.Sos => "Alerta SOS activada",
            _ => "Actividad de UniSave"
        };

        private string EstadoDelTrayecto(TrayectoCompartido trayecto)
        {
            if (trayecto.Fin.HasValue)
            {
                return trayecto.LlegadaConfirmada ? "Llegada confirmada" : "Dejó de compartirse";
            }

            return Avisos.Any(aviso => aviso.TrayectoId == trayecto.Id)
                ? "En curso" : "Sin actualizaciones";
        }

        private void Salir()
        {
            _vista = "Inicio";
            CancelarPulsacion();
            ActualizarContexto("Sesión cerrada.");
            CerrarHistorial(false);
            _cuenta = null;
            _alerta = false;
            _alertaId = null;
            _trayectoId = null;
            _inicioTrayecto = null;
            _trayecto = false;
            _destinoActivo = null;
            _destino = "";
            _familiar = "";
        }

        public async ValueTask DisposeAsync()
        {
            Salir();
            await _vida.CancelAsync();
            if (_actualizaciones is not null)
            {
                await _actualizaciones;
            }

            _vida.Dispose();
        }
    }
}
