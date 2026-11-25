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
        private Posicion? _ultimaPosicion;
        private string _estadoAutoridades = "";
        private int _dias = 7;
        private string _categoria = Seguimiento.Categorias[0];
        private string? _mapaDibujado;

        [Inject] private RepositorioCuentas Cuentas { get; set; } = null!;
        [Inject] private PasswordHasher<Cuenta> Cifrador { get; set; } = null!;
        [Inject] private Seguimiento Seguimiento { get; set; } = null!;
        [Inject] private ReceptorAutoridades Autoridades { get; set; } = null!;
        [Inject] private IJSRuntime Navegador { get; set; } = null!;

        private IReadOnlyList<Viaje> Viajes => _cuenta is null ? [] :
            Seguimiento.ConsultarViajes(_cuenta.Correo);

        private IReadOnlyList<Zona> Zonas => Seguimiento.ConsultarZonas(_dias);

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
            Seguimiento.Cambio += AlCambiarSeguimiento;
            _actualizaciones = ActualizarAsync(_vida.Token);
        }

        // Programa el redibujado sin esperar, y solo si el cambio toca a esta cuenta: con cientos
        // de vistas abiertas, redibujar todas en cada cambio saturaría el servidor.
        private void AlCambiarSeguimiento(IReadOnlyCollection<string>? afectados)
        {
            if (!_vida.IsCancellationRequested && _cuenta is { } cuenta &&
                (afectados is null || afectados.Contains(cuenta.Correo)))
            {
                _ = RedibujarAsync();
            }
        }

        private async Task RedibujarAsync()
        {
            try
            {
                await InvokeAsync(StateHasChanged);
            }
            catch (ObjectDisposedException)
            {
                // La vista se cerró mientras llegaba el aviso.
            }
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
                    var (resultado, cuenta) = await Cuentas.VerificarAsync(correo, _clave,
                        _vida.Token);
                    if (cuenta is null)
                    {
                        _mensaje = resultado == ResultadoAcceso.Bloqueado
                            ? "Demasiados intentos fallidos. Espera 15 minutos."
                            : "El correo o la contraseña son incorrectos.";
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
            if (_trayecto || _cuenta is not { EsEstudiante: true } cuenta)
            {
                return;
            }

            _trayecto = true;
            _trayectoId = Guid.NewGuid();
            _inicioTrayecto = DateTimeOffset.UtcNow;
            _destinoActivo = _destino.Trim();
            GuardarCambio(() => Seguimiento.IniciarViaje(new Viaje(_trayectoId.Value, cuenta.Correo,
                _destinoActivo.Length > 0 ? _destinoActivo : null, _inicioTrayecto.Value, null,
                false)), "Trayecto iniciado.");
            await PublicarAsync(true);
        }

        private void FinalizarTrayecto()
        {
            _trayecto = false;
            _destinoActivo = null;
            ActualizarContexto("Trayecto finalizado.");
            CerrarHistorial(Consiente);
            CerrarViaje(true);
            _trayectoId = null;
            _inicioTrayecto = null;
        }

        private void FinalizarAlerta()
        {
            _alerta = false;
            _alertaId = null;
            _estadoAutoridades = "";
            ActualizarContexto("Alerta finalizada.");
        }

        private void CerrarViaje(bool llegada)
        {
            if (_cuenta is { } cuenta && _trayectoId is { } id)
            {
                GuardarCambio(() => Seguimiento.CerrarViaje(cuenta.Correo, id, llegada), _mensaje);
            }
        }

        // La posición solo viaja si hay consentimiento; sin él, el reporte sale sin ubicación.
        private async Task ReportarAutoridadesAsync()
        {
            if (_cuenta is not { } cuenta || _alertaId is not { } alerta)
            {
                return;
            }

            var resultado = await Autoridades.EnviarAsync(alerta, cuenta.Correo,
                Consiente ? _ultimaPosicion : null, _vida.Token);
            _estadoAutoridades = resultado switch
            {
                ResultadoReporte.Recibido =>
                    $"El receptor de autoridades recibió el reporte ({DateTime.Now:t})",
                ResultadoReporte.Fallido =>
                    "No se pudo entregar el reporte a autoridades. Si estás en peligro, llama al 911.",
                _ => "El canal a autoridades no está configurado. Si estás en peligro, llama al 911."
            };
        }

        private async Task ReportarZonaAsync()
        {
            if (_cuenta is not { EsEstudiante: true } cuenta)
            {
                return;
            }

            try
            {
                var posicion = await Navegador.InvokeAsync<Posicion?>("unisave.posicion",
                    _vida.Token, true);
                _mensaje = posicion is null
                    ? "No se pudo obtener tu ubicación. Revisa el permiso del navegador."
                    : Seguimiento.ReportarZona(cuenta.Correo, _categoria, posicion)
                        ? "Gracias. Registramos la zona aproximada, no tu ubicación exacta."
                        : "Ya reportaste esta zona en las últimas 24 horas.";
            }
            catch (Exception error) when (error is JSException or IOException or ArgumentException)
            {
                _mensaje = "No se pudo registrar el reporte.";
            }
        }

        // Dibuja el mapa de la vista abierta; solo llama al navegador cuando cambian los datos.
        protected override async Task OnAfterRenderAsync(bool primeraVez)
        {
            string? mapa = _cuenta switch
            {
                { EsEstudiante: true } when _vista == "Mapa" => "mapa-estudiante",
                { EsEstudiante: false } when _vista == "Inicio" => "mapa-familiar",
                _ => null
            };
            if (mapa is null)
            {
                _mapaDibujado = null;
                return;
            }

            var datos = new
            {
                Marcadores = Avisos.Where(aviso => aviso.Posicion is not null).Select(aviso => new
                {
                    aviso.Posicion!.Latitud,
                    aviso.Posicion.Longitud,
                    aviso.Alerta,
                    Texto = aviso.Estudiante + (aviso.Alerta ? " · ALERTA SOS" : "")
                }),
                Zonas
            };
            string firma = mapa + System.Text.Json.JsonSerializer.Serialize(datos);
            if (firma == _mapaDibujado)
            {
                return;
            }

            _mapaDibujado = firma;
            try
            {
                await Navegador.InvokeVoidAsync("unisave.dibujarMapa", _vida.Token, mapa, datos);
            }
            catch (Exception error) when (error is JSException or JSDisconnectedException or
                OperationCanceledException)
            {
                // Sin mapa la vista sigue mostrando la lista y el enlace externo.
            }
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
                // La pulsación terminó: esperar el reporte a autoridades no debe bloquear la siguiente alerta.
                _pulsacion = null;
                _alerta = true;
                _alertaId = Guid.NewGuid();
                _mensaje = "Alerta activa.";
                await PublicarAsync(true);
                await ReportarAutoridadesAsync();
            }
            catch (OperationCanceledException) when (pulsacion.IsCancellationRequested)
            {
                _mensaje = "Pulsación incompleta.";
            }
            finally
            {
                if (_pulsacion == pulsacion)
                {
                    _pulsacion = null;
                }
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
                _ultimaPosicion = posicion ?? _ultimaPosicion;
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
            CerrarViaje(false);
            _ultimaPosicion = null;
            _estadoAutoridades = "";
            _cuenta = null;
            _alerta = false;
            _alertaId = null;
            _trayectoId = null;
            _inicioTrayecto = null;
            _trayecto = false;
            _destinoActivo = null;
            _destino = "";
            _familiar = "";
            _crear = false;
        }

        public async ValueTask DisposeAsync()
        {
            Seguimiento.Cambio -= AlCambiarSeguimiento;
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
