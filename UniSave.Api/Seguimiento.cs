// Archivo: Seguimiento.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Seguimiento
// Descripción: Gestiona vínculos, consentimiento, historial y zonas de riesgo.
// Fecha: 2026-11-15
// Trazabilidad: CU-02, CU-04, CU-06, CU-07, RF-06 y RF-07 (propuestos)

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

    /// <summary>Viaje del estudiante, guardado aunque no se comparta con nadie.</summary>
    public sealed record Viaje(Guid Id, string Estudiante, string? Destino,
        DateTimeOffset Inicio, DateTimeOffset? Fin, bool LlegadaConfirmada);

    /// <summary>Alerta o reporte guardado solo con la celda aproximada, nunca con la posición exacta.</summary>
    public sealed record Incidente(Guid Origen, string Cuenta, string Categoria,
        double Latitud, double Longitud, DateTimeOffset Fecha);

    /// <summary>Celda con incidentes de varias personas; es lo único que ven los usuarios.</summary>
    public sealed record Zona(double Latitud, double Longitud, int Incidentes, DateTimeOffset Ultimo);

    public sealed class Seguimiento
    {
        private sealed record Consentimiento(string Correo, Guid Version);

        private sealed record Registro(Invitacion[] Invitaciones, TrayectoCompartido[] Trayectos,
            Notificacion[] Notificaciones, Consentimiento[]? Consentimientos = null,
            Viaje[]? Viajes = null, Incidente[]? Incidentes = null);

        public static readonly string[] Categorias =
            ["Acoso", "Robo o asalto", "Poca iluminación", "Otro"];

        public const string CategoriaAlerta = "Alerta SOS";

        // Unos 150 m de latitud: suficiente para ubicar una calle sin señalar una casa.
        private const double Celda = 0.0015;

        private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan Retencion = TimeSpan.FromDays(30);
        private readonly object _acceso = new();
        private readonly Dictionary<string, (Guid Sesion, Aviso Aviso)> _avisos = [];
        private readonly string _archivo;
        private readonly int _minimoPersonas;
        private Registro _registro;

        /// <summary>Se produce cuando cambia un vínculo, una actividad o una notificación.</summary>
        /// <remarks>Los suscriptores no deben bloquear: solo programan su actualización.</remarks>
        public event Action? Cambio;

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
            _minimoPersonas = Math.Max(1, configuracion.GetValue("UniSave:MinimoPersonasZona", 3));
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

            AvisarCambio();
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

                // La primera posición de cada alerta alimenta el mapa de zonas de riesgo.
                bool incidente = aviso.AlertaId is { } idAlerta && aviso.Posicion is not null &&
                    !(_registro.Incidentes ?? []).Any(actual => actual.Origen == idAlerta);
                if (trayectos.Count > 0 || notificaciones.Count > 0 || reanudados.Count > 0 ||
                    incidente)
                {
                    var anteriores = _registro.Trayectos.Select(trayecto =>
                        reanudados.Contains((trayecto.Id, trayecto.Familiar))
                            ? trayecto with { Fin = null } : trayecto);
                    Guardar(_registro with
                    {
                        Trayectos = [.. anteriores, .. trayectos],
                        Notificaciones = [.. _registro.Notificaciones, .. notificaciones],
                        Incidentes = incidente
                            ? Agregar(aviso.AlertaId!.Value, aviso.Estudiante, CategoriaAlerta,
                                aviso.Posicion!)
                            : _registro.Incidentes
                    });
                }

                _avisos[aviso.Estudiante] = (sesion, aviso);
            }

            AvisarCambio();
            return true;
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
            bool detenido;
            lock (_acceso)
            {
                detenido = _avisos.TryGetValue(correo, out var actual) && actual.Sesion == sesion &&
                    _avisos.Remove(correo);
            }

            if (detenido)
            {
                AvisarCambio();
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

        /// <summary>Registra el inicio de un viaje en el historial propio del estudiante.</summary>
        /// <exception cref="IOException">No se pudo guardar el viaje.</exception>
        public void IniciarViaje(Viaje viaje)
        {
            lock (_acceso)
            {
                Guardar(_registro with { Viajes = [.. _registro.Viajes ?? [], viaje] });
            }
        }

        /// <summary>Cierra el viaje propio; la llegada solo se confirma con «Ya llegué».</summary>
        /// <exception cref="IOException">No se pudo guardar el cierre.</exception>
        public void CerrarViaje(string estudiante, Guid id, bool llegada)
        {
            lock (_acceso)
            {
                var viajes = _registro.Viajes ?? [];
                if (!viajes.Any(viaje => viaje.Id == id && viaje.Estudiante == estudiante &&
                    viaje.Fin is null))
                {
                    return;
                }

                DateTimeOffset fin = DateTimeOffset.UtcNow;
                Guardar(_registro with
                {
                    Viajes = viajes.Select(viaje => viaje.Id == id && viaje.Estudiante == estudiante
                        ? viaje with { Fin = fin, LlegadaConfirmada = llegada } : viaje).ToArray()
                });
            }
        }

        public IReadOnlyList<Viaje> ConsultarViajes(string estudiante)
        {
            lock (_acceso)
            {
                return (_registro.Viajes ?? []).Where(viaje => viaje.Estudiante == estudiante)
                    .OrderByDescending(viaje => viaje.Inicio).ToArray();
            }
        }

        /// <summary>Guarda el reporte de una zona insegura con su celda aproximada.</summary>
        /// <returns>Falso si la cuenta ya reportó esa celda en las últimas 24 horas.</returns>
        /// <exception cref="ArgumentException">La categoría no está en la lista permitida.</exception>
        /// <exception cref="IOException">No se pudo guardar el reporte.</exception>
        public bool ReportarZona(string cuenta, string categoria, Posicion posicion)
        {
            if (!Categorias.Contains(categoria))
            {
                throw new ArgumentException("Categoría no válida.", nameof(categoria));
            }

            lock (_acceso)
            {
                (double latitud, double longitud) = (Ajustar(posicion.Latitud),
                    Ajustar(posicion.Longitud));
                DateTimeOffset limite = DateTimeOffset.UtcNow.AddDays(-1);
                if ((_registro.Incidentes ?? []).Any(actual => actual.Cuenta == cuenta &&
                    actual.Latitud == latitud && actual.Longitud == longitud &&
                    actual.Fecha > limite))
                {
                    return false;
                }

                Guardar(_registro with
                {
                    Incidentes = Agregar(Guid.NewGuid(), cuenta, categoria, posicion)
                });
                return true;
            }
        }

        /// <summary>Agrupa los incidentes recientes por celda.</summary>
        /// <param name="dias">Días hacia atrás que se consideran.</param>
        /// <returns>Solo las celdas con incidentes de al menos el mínimo de personas.</returns>
        public IReadOnlyList<Zona> ConsultarZonas(int dias)
        {
            DateTimeOffset desde = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(dias, 1, 30));
            lock (_acceso)
            {
                return (_registro.Incidentes ?? []).Where(incidente => incidente.Fecha >= desde)
                    .GroupBy(incidente => (incidente.Latitud, incidente.Longitud))
                    .Where(celda => celda.Select(incidente => incidente.Cuenta).Distinct().Count()
                        >= _minimoPersonas)
                    .Select(celda => new Zona(celda.Key.Latitud, celda.Key.Longitud,
                        celda.Count(), celda.Max(incidente => incidente.Fecha)))
                    .OrderByDescending(zona => zona.Incidentes).ToArray();
            }
        }

        // Centro de la celda: todos los incidentes de la misma calle caen en el mismo punto.
        private static double Ajustar(double grados) =>
            Math.Round((Math.Floor(grados / Celda) + 0.5) * Celda, 6);

        // Agrega un incidente y descarta los que superan el periodo de retención.
        private Incidente[] Agregar(Guid origen, string cuenta, string categoria, Posicion posicion)
        {
            DateTimeOffset ahora = DateTimeOffset.UtcNow;
            return [.. (_registro.Incidentes ?? []).Where(actual => ahora - actual.Fecha < Retencion),
                new Incidente(origen, cuenta, categoria, Ajustar(posicion.Latitud),
                    Ajustar(posicion.Longitud), ahora)];
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
            AvisarCambio();
        }

        // Los portales abiertos se actualizan al instante en lugar de esperar su consulta periódica.
        private void AvisarCambio() => Cambio?.Invoke();
    }
}
