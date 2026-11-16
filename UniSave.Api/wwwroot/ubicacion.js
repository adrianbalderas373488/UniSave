// Archivo: ubicacion.js
// Autor: Adrian Balderas y Fernanda Itzel
// Materia: Verificación y Validación del Software
// Módulo: Ubicación
// Descripción: Consulta el GPS mediante la API del navegador.
// Fecha: 2026-11-09
// Trazabilidad: CU-04, CU-06, CU-07

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
        if (posicion) {
            this.marcarPropia(posicion);
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
    mapas: {},

    // Crea el mapa una sola vez por contenedor; Leaflet llega desde el CDN.
    obtenerMapa(id) {
        const contenedor = document.getElementById(id);
        if (!contenedor || !window.L) {
            return null;
        }
        let actual = this.mapas[id];
        if (!actual || actual.contenedor !== contenedor) {
            const mapa = L.map(contenedor).setView([31.8667, -116.5964], 13);
            L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
                maxZoom: 19,
                attribution: "&copy; OpenStreetMap"
            }).addTo(mapa);
            actual = { contenedor, mapa, capa: L.layerGroup().addTo(mapa), propia: null, centrado: false };
            this.mapas[id] = actual;
        }
        // El contenedor estuvo oculto: Leaflet debe recalcular su tamaño.
        actual.mapa.invalidateSize();
        return actual;
    },

    dibujarMapa(id, datos) {
        const actual = this.obtenerMapa(id);
        if (!actual) {
            return;
        }
        actual.capa.clearLayers();
        for (const zona of datos.zonas) {
            L.circle([zona.latitud, zona.longitud], {
                radius: 60 + 15 * Math.min(zona.incidentes, 10),
                color: "#ff4d5e",
                fillColor: "#ff4d5e",
                fillOpacity: 0.35
            }).bindTooltip(`${zona.incidentes} incidente(s)`).addTo(actual.capa);
        }
        const puntos = datos.marcadores.map((marcador) => {
            L.circleMarker([marcador.latitud, marcador.longitud], {
                radius: 9,
                color: "#ffffff",
                weight: 2,
                fillColor: marcador.alerta ? "#ff4d5e" : "#8be0cb",
                fillOpacity: 1
            }).bindTooltip(marcador.texto).addTo(actual.capa);
            return [marcador.latitud, marcador.longitud];
        });
        // Se centra una vez; después la persona puede moverse por el mapa sin que salte.
        if (puntos.length > 0 && !actual.centrado) {
            actual.mapa.fitBounds(puntos, { maxZoom: 16, padding: [30, 30] });
            actual.centrado = true;
        }
    },

    marcarPropia(posicion) {
        const actual = this.obtenerMapa("mapa-estudiante");
        if (!actual) {
            return;
        }
        const punto = [posicion.latitud, posicion.longitud];
        if (actual.propia) {
            actual.propia.setLatLng(punto);
        } else {
            actual.propia = L.circleMarker(punto, {
                radius: 9, color: "#ffffff", weight: 2, fillColor: "#8be0cb", fillOpacity: 1
            }).bindTooltip("Tú").addTo(actual.mapa);
        }
        actual.mapa.setView(punto, 16);
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
