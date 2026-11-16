# UniSave

Aplicación de acompañamiento universitario desarrollada en C# y Razor. La solución contiene
el dominio compartido, el servidor ASP.NET Core con portal Blazor y el cliente .NET MAUI
Blazor Hybrid para Android e iOS.

## Ejecutar la demostración en la computadora

Con el SDK de .NET 10, abre una terminal en esta carpeta y ejecuta:

```powershell
dotnet run --project UniSave.Api --no-launch-profile --urls http://127.0.0.1:5189
```

Abre [UniSave local](http://127.0.0.1:5189/) en dos ventanas. Registra dos cuentas de prueba
con contraseñas distintas de las que uses en otros servicios. Usa un correo de ejemplo con
dominio `@uabc.edu.mx` para el rol estudiante y un correo de ejemplo de otro dominio para
el rol familiar. UniSave reconoce el dominio exacto sin distinguir mayúsculas. La demostración
clasifica la dirección por dominio y carece de verificación del buzón.

1. En el perfil estudiante, envía una invitación al correo registrado por el familiar.
2. Abre el perfil familiar y acepta la invitación.
3. En el perfil estudiante, acepta compartir ubicación e inicia un trayecto. El navegador
   solicita permiso para consultar la posición.
4. Comprueba la actividad en la cuenta familiar. Si falta permiso o GPS, aparece el aviso
   sin coordenadas. El mapa externo se abre únicamente al seleccionar el enlace.
5. Retira el consentimiento o finaliza la actividad y comprueba que desaparezca del perfil
   familiar en su siguiente actualización.

El portal necesita conexión con el servidor mientras se comparte. Los avisos se ven en la
cuenta familiar con la página abierta. La campana muestra las notificaciones de la cuenta.
La pestaña Historial muestra el destino, las horas y el estado de los viajes compartidos con
ese familiar. Al quitar un vínculo se retira el historial asociado.

Las cuentas y los resúmenes de contraseña se guardan en `UniSave.Api/datos/cuentas.json`.
Los vínculos, el consentimiento del perfil, las notificaciones y el historial se guardan en
`seguimiento.json` junto al archivo de cuentas.
Puede configurarse otra ruta con `UniSave__ArchivoSeguimiento`. El historial guarda el
resumen del viaje. Las coordenadas quedan en memoria y dejan de estar disponibles al
retirar el consentimiento o vencer la actualización.

## Mapa en vivo, zonas de riesgo y viajes

- **Mapa en vivo.** El portal dibuja el mapa con Leaflet 1.9.4 (CDN de cdnjs) y mosaicos de
  OpenStreetMap, por lo que necesita Internet. El familiar ve el marcador del estudiante
  moverse con cada publicación (cada 5 s) y en rojo durante una alerta.
- **Zonas de riesgo.** En Mapa, el estudiante puede reportar su zona actual (acoso, robo o
  asalto, poca iluminación u otro). Las alertas SOS con ubicación también cuentan. Solo se
  guarda la celda aproximada de unos 150 m, nunca la posición exacta; los incidentes se
  descartan a los 30 días. Un círculo rojo aparece cuando la celda tiene incidentes de al
  menos `UniSave__MinimoPersonasZona` personas distintas (3 por omisión). Se puede filtrar
  por el último día, 7 o 30 días.
- **Mis viajes.** El estudiante ve su propio historial, aunque no comparta el viaje.

## Reporte a autoridades

Con `UniSave__ReceptorAutoridades` apuntando a una dirección http(s), cada alerta envía un
POST JSON con el tipo, la alerta, el estudiante, la posición (solo con consentimiento) y la
fecha. La pantalla muestra el resultado real del envío. **Usa solo un receptor de prueba
acordado con el cliente; nunca un servicio de emergencias real.** Sin configurar, la pantalla
indica que el canal no está configurado.

## Inicio de sesión

Tras 5 contraseñas incorrectas para un mismo correo registrado, UniSave bloquea ese correo
durante 15 minutos, tanto en el portal como en la API (`/cuentas/sesion` responde 429). El
conteo vive en la memoria del servidor. Además, la API admite 20 peticiones de cuentas por
minuto **por dirección IP**; detrás de un proxy hay que configurar ForwardedHeaders para que
el servidor vea la IP real.

## Android y Safari

Para abrir el portal desde un teléfono, usa una dirección HTTPS que el teléfono pueda
alcanzar y cuyo certificado reconozca. `127.0.0.1` en el teléfono apunta al propio teléfono.
El servidor puede ejecutarse en una computadora sin contratar alojamiento. Para configurar
una dirección de prueba usa un certificado existente y la configuración de Kestrel o un
proxy HTTPS de la institución. Para preparar el APK, el equipo de desarrollo configura
la dirección HTTPS de la API en la compilación. El usuario solo escribe su correo y
contraseña.

```powershell
$unisaveServidor = Read-Host 'Dirección HTTPS de la API de UniSave'
dotnet build UniSave.Movil -p:UniSavePlataforma=net10.0-android "-p:UniSaveServidor=$unisaveServidor"
```

La compilación sin esta configuración sirve para revisar la interfaz. El inicio de
sesión en el APK requiere que el equipo configure la dirección y que el teléfono pueda
acceder a ella.

El cliente Android permite iniciar sesión con la API, administrar contactos locales,
activar una alerta con una pulsación sostenida de dos segundos, iniciar un trayecto,
consultar la posición y preparar un SMS para que la persona lo envíe desde su app de
mensajes. El operador determina si el SMS tiene costo. La invitación y el seguimiento
entre cuentas se demuestran en el portal web. La prueba nativa de iOS requiere Mac con Xcode.

## Alcance pendiente

- Receptor real de autoridades: requiere acuerdo con el cliente y la institución.
- Mapa, zonas de riesgo y «Mis viajes» en la aplicación móvil (hoy solo en el portal).
- Validar con el cliente RF-06 (zonas de riesgo) y RF-07 (historial del estudiante).
- Verificación de correo y notificaciones push.
- Configuración del acceso HTTPS desde teléfonos y pruebas físicas de permisos, GPS y SMS.
- Prueba con Safari en un iPhone. La verificación en el navegador de desarrollo se realiza
  en la computadora.
