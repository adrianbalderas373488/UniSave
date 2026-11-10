// Archivo: Seguimiento.cs
// Autor: Equipo UniSave (implementación asistida por Codex)
// Materia: Verificación y Validación del Software
// Módulo: Seguimiento
// Descripción: Gestiona vínculos, consentimiento e historial.
// Fecha: 2026-11-09
// Trazabilidad: CU-02, CU-04, CU-06, CU-07

using System.Text.Json;

namespace UniSave.Api
{
    public sealed record Invitacion(string Estudiante, string Familiar, bool Aceptada);

    public sealed record Posicion(double Latitud, double Longitud, DateTimeOffset Fecha);

    public sealed record Aviso(string Estudiante, bool Alerta, bool Trayecto,
        string? Destino, Posicion? Posicion, DateTimeOffset Actualizado,
        Guid? TrayectoId, DateTimeOffset? Inicio, Guid? AlertaId);

    public sealed record TrayectoCompartido(Guid Id, string Estudiante, string Familiar,
        string? Destino, DateTimeOffset Inicio, DateTimeOffset? Fin, bool LlegadaConfirmada);

    public enum TipoNotificacion
    {
        Invitacion,
        Aceptacion,
        Inicio,
        Llegada,
        Sos
    }

    public sealed record Notificacion(Guid Origen, string Destinatario, string Contacto,
        TipoNotificacion Tipo, DateTimeOffset Fecha, bool Leida);

    public sealed class Seguimiento
    {
        private sealed record Consentimiento(string Correo, Guid Version);

        private sealed record Registro(Invitacion[] Invitaciones, TrayectoCompartido[] Trayectos,
            Notificacion[] Notificaciones, Consentimiento[]? Consentimientos = null);

        private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(30);
        private readonly object _acceso = new();
        private readonly Dictionary<string, (Guid Sesion, Aviso Aviso)> _avisos = [];
        private readonly string _archivo;
        private Registro _registro;

        public Seguimiento(IConfiguration configuracion, IWebHostEnvironment entorno)
        {
            string cuentas = Path.GetFullPath(
                configuracion["UniSave:ArchivoCuentas"] ?? "datos/cuentas.json",
                entorno.ContentRootPath);
            _archivo = Path.GetFullPath(configuracion["UniSave:ArchivoSeguimiento"] ??
                Path.Combine(Path.GetDirectoryName(cuentas)!, "seguimiento.json"),
                entorno.ContentRootPath);
            _registro = File.Exists(_archivo)
                ? JsonSerializer.Deserialize<Registro>(File.ReadAllText(_archivo))
                    ?? throw new InvalidDataException("No se pudo leer el registro de seguimiento.")
                : new Registro([], [], []);
        }

        /// <summary>Devuelve la versión de la autorización vigente para compartir.</summary>
        public Guid? ConsultarConsentimiento(string correo)
        {
            lock (_acceso)
            {
                return BuscarConsentimiento(correo)?.Version;
            }
        }

        /// <summary>Guarda el permiso por cuenta y corta el seguimiento al retirarlo.</summary>
        /// <param name="correo">Correo de la cuenta titular.</param>
        /// <param name="permitir">Indica si el titular autorizó compartir su ubicación.</param>
        /// <exception cref="IOException">No se pudo guardar la decisión.</exception>
        public void EstablecerConsentimiento(string correo, bool permitir)
        {
            lock (_acceso)
            {
                if ((BuscarConsentimiento(correo) is not null) == permitir)
                {
                    return;
                }

                var otros = (_registro.Consentimientos ?? [])
                    .Where(actual => actual.Correo != correo);
                DateTimeOffset ahora = DateTimeOffset.UtcNow;
                Guardar(_registro with
                {
                    Consentimientos = permitir
                        ? [.. otros, new Consentimiento(correo, Guid.NewGuid())]
                        : otros.ToArray(),
                    Trayectos = permitir ? _registro.Trayectos : _registro.Trayectos
                        .Select(viaje => viaje.Estudiante == correo && viaje.Fin is null
                            ? viaje with { Fin = ahora } : viaje).ToArray()
                });

                if (!permitir)
                {
                    _avisos.Remove(correo);
                }
            }
        }

        /// <summary>Consulta los vínculos de la cuenta indicada.</summary>
        public IReadOnlyList<Invitacion> ConsultarInvitaciones(string correo)
        {
            lock (_acceso)
            {
                return _registro.Invitaciones.Where(invitacion =>
                    invitacion.Estudiante == correo || invitacion.Familiar == correo).ToArray();
            }
        }

        public bool Invitar(string estudiante, string familiar)
        {
            lock (_acceso)
            {
                if (BuscarInvitacion(estudiante, familiar) is not null)
                {
                    return false;
                }

                Guardar(_registro with
                {
                    Invitaciones = [.. _registro.Invitaciones,
                        new Invitacion(estudiante, familiar, false)],
                    Notificaciones = [.. _registro.Notificaciones,
                        Nueva(Guid.NewGuid(), familiar, estudiante, TipoNotificacion.Invitacion)]
                });
                return true;
            }
        }

        public void Responder(string familiar, string estudiante, bool aceptar)
        {
            lock (_acceso)
            {
                var invitacion = BuscarInvitacion(estudiante, familiar);
                if (invitacion is null || invitacion.Aceptada)
                {
                    return;
                }

                Invitacion[] invitaciones = aceptar
                    ? _registro.Invitaciones.Select(actual => actual == invitacion
                        ? actual with { Aceptada = true } : actual).ToArray()
                    : _registro.Invitaciones.Where(actual => actual != invitacion).ToArray();
                var avisos = _registro.Notificaciones.Select(aviso =>
                    aviso.Destinatario == familiar && aviso.Contacto == estudiante &&
                    aviso.Tipo == TipoNotificacion.Invitacion
                        ? aviso with { Leida = true } : aviso).ToArray();
                Guardar(_registro with
                {
                    Invitaciones = invitaciones,
                    Notificaciones = aceptar
                        ? [.. avisos, Nueva(Guid.NewGuid(), estudiante, familiar,
                            TipoNotificacion.Aceptacion)]
                        : avisos
                });
            }
        }

        public void Desvincular(string correo, Invitacion invitacion)
        {
            if (correo != invitacion.Estudiante && correo != invitacion.Familiar)
            {
                return;
            }

            lock (_acceso)
            {
                var actual = BuscarInvitacion(invitacion.Estudiante, invitacion.Familiar);
                if (actual is null)
                {
                    return;
                }

                // Quitar un vínculo retira el acceso a su historial y a sus notificaciones.
                Guardar(_registro with
                {
                    Invitaciones = _registro.Invitaciones.Where(vinculo => vinculo != actual)
                        .ToArray(),
                    Trayectos = _registro.Trayectos.Where(trayecto =>
                        trayecto.Estudiante != actual.Estudiante ||
                        trayecto.Familiar != actual.Familiar).ToArray(),
                    Notificaciones = _registro.Notificaciones.Where(aviso =>
                        !(aviso.Destinatario == actual.Familiar &&
                        aviso.Contacto == actual.Estudiante) &&
                        !(aviso.Destinatario == actual.Estudiante &&
                        aviso.Contacto == actual.Familiar)).ToArray()
                });
            }
        }

        /// <summary>Actualiza la posición en memoria y registra eventos por destinatario.</summary>
        /// <param name="sesion">Identificador de la sesión que inició el seguimiento.</param>
        /// <param name="versionConsentimiento">Versión del permiso del titular.</param>
        /// <param name="aviso">Estado del trayecto o de la alerta activa.</param>
        /// <returns>Verdadero si el consentimiento seguía vigente al publicar.</returns>
        /// <exception cref="IOException">No se pudo guardar el evento por destinatario.</exception>
        public bool Publicar(Guid sesion, Guid versionConsentimiento, Aviso aviso)
        {
            lock (_acceso)
            {
                if (BuscarConsentimiento(aviso.Estudiante)?.Version != versionConsentimiento)
                {
                    return false;
                }

                var trayectos = new List<TrayectoCompartido>();
                var notificaciones = new List<Notificacion>();
                var reanudados = new HashSet<(Guid, string)>();
                foreach (var vinculo in _registro.Invitaciones.Where(vinculo =>
                    vinculo.Aceptada && vinculo.Estudiante == aviso.Estudiante))
                {
                    if (aviso.TrayectoId is { } viaje && aviso.Inicio is { } inicio)
                    {
                        var anterior = _registro.Trayectos.FirstOrDefault(actual =>
                            actual.Id == viaje && actual.Familiar == vinculo.Familiar);
                        if (anterior is null)
                        {
                            trayectos.Add(new TrayectoCompartido(viaje, aviso.Estudiante,
                                vinculo.Familiar, aviso.Destino, inicio, null, false));
                            notificaciones.Add(Nueva(viaje, vinculo.Familiar, aviso.Estudiante,
                                TipoNotificacion.Inicio));
                        }
                        else if (anterior.Fin.HasValue && !anterior.LlegadaConfirmada)
                        {
                            reanudados.Add((viaje, vinculo.Familiar));
                        }
                    }

                    if (aviso.AlertaId is { } alerta && !_registro.Notificaciones.Any(actual =>
                        actual.Origen == alerta && actual.Destinatario == vinculo.Familiar &&
                        actual.Tipo == TipoNotificacion.Sos))
                    {
                        notificaciones.Add(Nueva(alerta, vinculo.Familiar, aviso.Estudiante,
                            TipoNotificacion.Sos));
                    }
                }

                if (trayectos.Count > 0 || notificaciones.Count > 0 || reanudados.Count > 0)
                {
                    var anteriores = _registro.Trayectos.Select(trayecto =>
                        reanudados.Contains((trayecto.Id, trayecto.Familiar))
                            ? trayecto with { Fin = null } : trayecto);
                    Guardar(_registro with
                    {
                        Trayectos = [.. anteriores, .. trayectos],
                        Notificaciones = [.. _registro.Notificaciones, .. notificaciones]
                    });
                }

                _avisos[aviso.Estudiante] = (sesion, aviso);
                return true;
            }
        }

        /// <summary>Cierra el historial del viaje compartido sin guardar sus posiciones.</summary>
        public void CerrarTrayecto(string estudiante, Guid id, bool confirmarLlegada)
        {
            lock (_acceso)
            {
                var abiertos = _registro.Trayectos.Where(trayecto =>
                    trayecto.Id == id && trayecto.Estudiante == estudiante &&
                    trayecto.Fin is null).ToArray();
                if (abiertos.Length == 0)
                {
                    return;
                }

                confirmarLlegada = confirmarLlegada && BuscarConsentimiento(estudiante) is not null;
                DateTimeOffset fin = DateTimeOffset.UtcNow;
                Guardar(_registro with
                {
                    Trayectos = _registro.Trayectos.Select(trayecto => abiertos.Contains(trayecto)
                        ? trayecto with { Fin = fin, LlegadaConfirmada = confirmarLlegada }
                        : trayecto).ToArray(),
                    Notificaciones = confirmarLlegada
                        ? [.. _registro.Notificaciones, .. abiertos.Select(trayecto =>
                            Nueva(id, trayecto.Familiar, estudiante, TipoNotificacion.Llegada))]
                        : _registro.Notificaciones
                });
            }
        }

        public void Detener(string correo, Guid sesion)
        {
            lock (_acceso)
            {
                if (_avisos.TryGetValue(correo, out var actual) && actual.Sesion == sesion)
                {
                    _avisos.Remove(correo);
                }
            }
        }

        public IReadOnlyList<Aviso> ConsultarAvisos(string familiar)
        {
            lock (_acceso)
            {
                DateTimeOffset ahora = DateTimeOffset.UtcNow;
                foreach (var vencido in _avisos.Where(par =>
                    ahora - par.Value.Aviso.Actualizado > Vigencia)
                    .Select(par => par.Key).ToArray())
                {
                    _avisos.Remove(vencido);
                }

                return _registro.Invitaciones.Where(invitacion =>
                    invitacion.Familiar == familiar && invitacion.Aceptada &&
                    _avisos.ContainsKey(invitacion.Estudiante))
                    .Select(invitacion => _avisos[invitacion.Estudiante].Aviso).ToArray();
            }
        }

        public IReadOnlyList<TrayectoCompartido> ConsultarHistorial(string familiar)
        {
            lock (_acceso)
            {
                return _registro.Trayectos.Where(trayecto => trayecto.Familiar == familiar)
                    .OrderByDescending(trayecto => trayecto.Inicio).ToArray();
            }
        }

        public IReadOnlyList<Notificacion> ConsultarNotificaciones(string correo)
        {
            lock (_acceso)
            {
                return _registro.Notificaciones.Where(aviso => aviso.Destinatario == correo)
                    .OrderByDescending(aviso => aviso.Fecha).ToArray();
            }
        }

        public void MarcarLeidas(string correo)
        {
            lock (_acceso)
            {
                if (_registro.Notificaciones.Any(aviso => aviso.Destinatario == correo &&
                    !aviso.Leida))
                {
                    Guardar(_registro with
                    {
                        Notificaciones = _registro.Notificaciones.Select(aviso =>
                            aviso.Destinatario == correo ? aviso with { Leida = true } : aviso)
                            .ToArray()
                    });
                }
            }
        }

        private Invitacion? BuscarInvitacion(string estudiante, string familiar) =>
            _registro.Invitaciones.FirstOrDefault(actual =>
                actual.Estudiante == estudiante && actual.Familiar == familiar);

        private Consentimiento? BuscarConsentimiento(string correo) =>
            _registro.Consentimientos?.FirstOrDefault(actual => actual.Correo == correo);

        private static Notificacion Nueva(Guid origen, string destinatario, string contacto,
            TipoNotificacion tipo) =>
            new(origen, destinatario, contacto, tipo, DateTimeOffset.UtcNow, false);

        private void Guardar(Registro registro)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_archivo)!);
            string temporal = _archivo + ".tmp";
            File.WriteAllText(temporal, JsonSerializer.Serialize(registro));
            File.Move(temporal, _archivo, true);
            _registro = registro;
        }
    }
}
