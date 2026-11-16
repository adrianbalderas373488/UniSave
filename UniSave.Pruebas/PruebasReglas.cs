// Archivo: PruebasReglas.cs
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Pruebas
// Descripción: Prueba las reglas de privacidad, zonas, retención, avisos y acceso.
// Fecha: 2026-11-15
// Trazabilidad: RNF-01, RLR-01, RF-06, RF-07, CU-01 a CU-07

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using UniSave.Api;
using UniSave.Dominio;

namespace UniSave.Pruebas
{
    public sealed class PruebasReglas : IDisposable
    {
        private const string Estudiante = "alumna@uabc.edu.mx";
        private const string Familiar = "familia@example.com";
        private const string Clave = "PruebaUniSave2026!";
        private readonly string _carpeta = Directory.CreateTempSubdirectory("unisave-").FullName;

        public void Dispose() => Directory.Delete(_carpeta, true);

        [Theory]
        [InlineData("Alumna@UABC.edu.mx", true)]
        [InlineData("alumna@gmail.com", false)]
        [InlineData("alumna@fakeuabc.edu.mx", false)]
        [InlineData("alumna@uabc.edu.mx.mx", false)]
        public void Correo_reconoce_solo_el_dominio_institucional_exacto(string correo, bool esperado) =>
            Assert.Equal(esperado, Correo.EsInstitucional(correo));

        [Theory]
        [InlineData("Ana <ana@uabc.edu.mx>")]
        [InlineData("ana perez@uabc.edu.mx")]
        [InlineData("ana@localhost")]
        public void Correo_rechaza_formatos_no_validos(string correo) =>
            Assert.Null(Correo.Normalizar(correo));

        [Fact]
        public void Retirar_el_consentimiento_detiene_la_publicacion()
        {
            var seguimiento = CrearSeguimiento();
            Vincular(seguimiento);
            seguimiento.EstablecerConsentimiento(Estudiante, true);
            Guid version = seguimiento.ConsultarConsentimiento(Estudiante)!.Value;

            Assert.True(seguimiento.Publicar(Guid.NewGuid(), version, Aviso()));
            Assert.Single(seguimiento.ConsultarAvisos(Familiar));

            seguimiento.EstablecerConsentimiento(Estudiante, false);
            Assert.Empty(seguimiento.ConsultarAvisos(Familiar));
            // Una sesión que aún conserva el permiso anterior no puede volver a publicar.
            Assert.False(seguimiento.Publicar(Guid.NewGuid(), version, Aviso()));
            Assert.Empty(seguimiento.ConsultarAvisos(Familiar));
        }

        [Fact]
        public void Una_zona_aparece_con_tres_personas_y_sin_la_posicion_exacta()
        {
            var seguimiento = CrearSeguimiento();
            var posicion = new Posicion(31.86581, -116.59641, DateTimeOffset.UtcNow);

            Assert.True(seguimiento.ReportarZona("a@uabc.edu.mx", "Acoso", posicion));
            Assert.False(seguimiento.ReportarZona("a@uabc.edu.mx", "Acoso", posicion));
            Assert.True(seguimiento.ReportarZona("b@uabc.edu.mx", "Otro", posicion));
            Assert.Empty(seguimiento.ConsultarZonas(7));

            Assert.True(seguimiento.ReportarZona("c@uabc.edu.mx", "Otro", posicion));
            var zona = Assert.Single(seguimiento.ConsultarZonas(7));
            Assert.Equal(3, zona.Incidentes);
            Assert.NotEqual(posicion.Latitud, zona.Latitud);
            Assert.True(Math.Abs(posicion.Latitud - zona.Latitud) < 0.0015);
        }

        [Fact]
        public void Lo_cerrado_y_antiguo_se_descarta_al_cargar()
        {
            DateTimeOffset ahora = DateTimeOffset.UtcNow;
            File.WriteAllText(Path.Combine(_carpeta, "seguimiento.json"), JsonSerializer.Serialize(new
            {
                Invitaciones = Array.Empty<Invitacion>(),
                Trayectos = new[]
                {
                    new TrayectoCompartido(Guid.NewGuid(), Estudiante, Familiar, "Antiguo",
                        ahora.AddDays(-200), ahora.AddDays(-200), true),
                    new TrayectoCompartido(Guid.NewGuid(), Estudiante, Familiar, "Abierto",
                        ahora.AddDays(-200), null, false)
                },
                Notificaciones = new[]
                {
                    new Notificacion(Guid.NewGuid(), Familiar, Estudiante, TipoNotificacion.Inicio,
                        ahora.AddDays(-200), true),
                    new Notificacion(Guid.NewGuid(), Familiar, Estudiante, TipoNotificacion.Inicio,
                        ahora.AddDays(-1), false)
                }
            }));

            var seguimiento = CrearSeguimiento();

            Assert.Single(seguimiento.ConsultarNotificaciones(Familiar));
            Assert.Equal("Abierto", Assert.Single(seguimiento.ConsultarHistorial(Familiar)).Destino);
        }

        [Fact]
        public void El_aviso_de_cambio_solo_llega_a_las_cuentas_afectadas()
        {
            var seguimiento = CrearSeguimiento();
            var avisos = new List<IReadOnlyCollection<string>?>();
            seguimiento.Cambio += avisos.Add;

            seguimiento.Invitar(Estudiante, Familiar);
            Assert.Equal([Estudiante, Familiar], avisos[^1]!.Order());
            Assert.DoesNotContain("otra@example.com", avisos[^1]!);

            // Las zonas son comunes: un reporte avisa a todas las vistas.
            seguimiento.ReportarZona(Estudiante, "Otro", new Posicion(31.8, -116.6, DateTimeOffset.UtcNow));
            Assert.Null(avisos[^1]);
        }

        [Fact]
        public async Task El_bloqueo_aplica_a_cuentas_reales_y_no_a_las_demas()
        {
            var cifrador = new PasswordHasher<Cuenta>();
            var cuentas = new RepositorioCuentas(Configuracion(), new Entorno(_carpeta), cifrador);
            foreach (string correo in new[] { Estudiante, "otra@uabc.edu.mx" })
            {
                var cuenta = new Cuenta(correo, "", true);
                await cuentas.AgregarAsync(cuenta with { Resumen = cifrador.HashPassword(cuenta, Clave) },
                    CancellationToken.None);
            }

            for (int intento = 0; intento < 5; intento++)
            {
                var (fallido, _) = await cuentas.VerificarAsync(Estudiante, "Incorrecta2026!",
                    CancellationToken.None);
                Assert.Equal(ResultadoAcceso.Incorrecto, fallido);
            }

            var (bloqueado, _) = await cuentas.VerificarAsync(Estudiante, Clave, CancellationToken.None);
            var (otra, _) = await cuentas.VerificarAsync("otra@uabc.edu.mx", Clave, CancellationToken.None);
            Assert.Equal(ResultadoAcceso.Bloqueado, bloqueado);
            Assert.Equal(ResultadoAcceso.Correcto, otra);

            // Un correo sin cuenta nunca se bloquea: no ocupa memoria.
            for (int intento = 0; intento < 10; intento++)
            {
                var (inexistente, _) = await cuentas.VerificarAsync("nadie@uabc.edu.mx", Clave,
                    CancellationToken.None);
                Assert.Equal(ResultadoAcceso.Incorrecto, inexistente);
            }
        }

        private Seguimiento CrearSeguimiento() => new(Configuracion(), new Entorno(_carpeta));

        private IConfiguration Configuracion() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UniSave:ArchivoCuentas"] = Path.Combine(_carpeta, "cuentas.json"),
                ["UniSave:ArchivoSeguimiento"] = Path.Combine(_carpeta, "seguimiento.json")
            }).Build();

        private static void Vincular(Seguimiento seguimiento)
        {
            seguimiento.Invitar(Estudiante, Familiar);
            seguimiento.Responder(Familiar, Estudiante, true);
        }

        private static Aviso Aviso() => new(Estudiante, false, true, "Facultad", null,
            DateTimeOffset.UtcNow, Guid.NewGuid(), DateTimeOffset.UtcNow, null);

        private sealed class Entorno(string raiz) : IWebHostEnvironment
        {
            public string WebRootPath { get; set; } = raiz;
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string ApplicationName { get; set; } = "UniSave.Pruebas";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
            public string ContentRootPath { get; set; } = raiz;
            public string EnvironmentName { get; set; } = "Testing";
        }
    }
}
