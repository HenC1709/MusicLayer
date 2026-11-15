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
