// Archivo: ubicacion.js. Autor: Equipo UniSave. Materia: V&V del Software.
// Módulo: Ubicación. Descripción: Puente de permisos y GPS del navegador.
// Fecha: 2026-11-07. Trazabilidad: CU-04, CU-06, CU-07.
window.unisave = {
    async mostrarPosicion() {
        const posicion = await this.posicion(true);
        const salida = document.getElementById("posicion-local");
        const enlace = document.getElementById("mapa-local");
        if (salida) {
            salida.textContent = posicion
                ? `${posicion.latitud.toFixed(5)}, ${posicion.longitud.toFixed(5)}`
                : "Ubicación no disponible. Revisa los permisos del navegador.";
        }
        if (enlace) {
            enlace.hidden = !posicion;
            if (posicion) {
                const latitud = posicion.latitud.toFixed(5);
                const longitud = posicion.longitud.toFixed(5);
                const mapa = new URL("https://www.openstreetmap.org/");
                mapa.searchParams.set("mlat", latitud);
                mapa.searchParams.set("mlon", longitud);
                mapa.hash = `map=16/${latitud}/${longitud}`;
                enlace.href = mapa.href;
            } else {
                enlace.removeAttribute("href");
            }
        }
    },
    async posicion(solicitarPermiso) {
        if (!navigator.geolocation) {
            return null;
        }

        if (!solicitarPermiso) {
            try {
                const permiso = await navigator.permissions.query({ name: "geolocation" });
                if (permiso.state !== "granted") {
                    return null;
                }
            } catch {
                return null;
            }
        }

        return new Promise((resolver) => navigator.geolocation.getCurrentPosition(
            (posicion) => resolver({
                latitud: posicion.coords.latitude,
                longitud: posicion.coords.longitude,
                fecha: new Date(posicion.timestamp).toISOString()
            }),
            () => resolver(null),
            { timeout: 5000, maximumAge: 0 }
        ));
    }
};
