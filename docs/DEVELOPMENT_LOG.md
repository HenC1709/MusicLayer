# Bitácora de desarrollo

Agrega una entrada por cada error, bug o aprendizaje relevante.

## Plantilla de entrada

- Fecha:
- Sprint:
- Qué pasó:
- Causa:
- Solución:
- Qué aprendí:

## Inicio del Sprint 1 — 2026-11-15

- Se añadieron escaneo asíncrono, casos de uso y reproducción con NAudio mediante una ventana WPF mínima.
- El reproductor serializa cambios y cierre para liberar la salida y el lector anteriores.
- Pendiente: validación manual con audio real y distintos formatos.

## Revisión del Sprint 1

- Composición DI en App y estado de presentación en MainViewModel.
- Limpieza al finalizar audio y manejo de errores durante el cierre.
- Validación de rutas de Song y existencia del archivo antes de reproducir; escaneo recursivo.
- Se retiraron archivos vacíos de funcionalidades futuras. Se crearán al implementar su sprint.
- Se añadieron y ejecutaron ocho pruebas xUnit: dominio, archivos inexistentes, cancelación, escaneo recursivo y finalización del ViewModel con reproductor falso.

## Sprint 4, tarea 1 — Metadatos locales (2026-11-20)

- **Antes.** La biblioteca y el reproductor mostraban el nombre del archivo. TagLibSharp se usaba para la carátula FLAC.
- **Qué se hizo.** Se añadió un lector de título, artista y álbum. El escaneo lo usa en segundo plano y conserva por separado la ruta y los metadatos opcionales de cada pista.
- **Qué cambió al usar la app.** La biblioteca y la barra muestran el título de la etiqueta, con el nombre del archivo como respaldo. En **Ahora suena**, artista y álbum acompañan a la pista activa. Si las etiquetas están ausentes o ilegibles, la pista sigue en la biblioteca.
- **Verificación.** La solución compiló con 0 errores y 0 advertencias, y pasaron 45 pruebas xUnit. Se comprobó en WPF la importación, visualización y reproducción de dos FLAC de prueba, uno con etiquetas y otro sin ellas. Queda probar la presentación con una biblioteca real y etiquetas largas o incompletas.
- **Pendiente para LRCLIB.** Cliente HTTP y selección de coincidencias, parser LRC, caché, vista de letras sincronizadas y salto de reproducción. Para la futura consulta, el título y el artista salen de las etiquetas y la duración puede obtenerse del progreso de la pista activa.

## LRCLIB, tarea 2 — Cliente HTTP y coincidencias (2026-11-20)

- **Antes.** MusicLayer leía las etiquetas locales y todavía carecía de un servicio para buscar letras.
- **Qué se hizo.** Se añadió el contrato en Application, el cliente HTTP reutilizable en Infrastructure y su registro en el contenedor de WPF. El cliente identifica MusicLayer, consulta `/api/get` y permite una sola consulta de respaldo a `/api/search`. Las llamadas son secuenciales, aceptan cancelación y respetan el plazo de `Retry-After`.
- **Qué cambió.** Una pista con título y artista ya se puede resolver desde el servicio. Se descartan las diferencias de versión, de álbum cuando ambos están identificados y de duración superiores a dos segundos. Las coincidencias igual de probables quedan sin resolver. El resultado distingue letra disponible, instrumental, sin coincidencia y fallo temporal.
- **Verificación.** Compilación con 0 errores y 0 advertencias. Pasaron 66 pruebas xUnit, incluidas 21 nuevas con selección de versiones, respuestas HTTP simuladas, cancelación y plazos de reintento.
- **Pendiente para LRCLIB.** Parser LRC, caché local, vista WPF con cancelación al cambiar de canción y salto de reproducción. La consulta desde la interfaz se habilitará al integrar la vista de letras.
