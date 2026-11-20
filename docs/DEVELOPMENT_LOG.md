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

## LRCLIB, tarea 3 — Parser LRC y línea activa (2026-11-20)

- **Antes.** El cliente devolvía el texto plano y el texto LRC con sus marcas de tiempo.
- **Qué se hizo.** Application interpreta una o varias marcas por línea, fracciones de segundo y el ajuste global `offset` en milisegundos. Ordena las líneas por tiempo, descarta las entradas sin marca válida y conserva las líneas vacías con tiempo para representar pausas en la letra.
- **Qué cambió.** La posición actual permite obtener el índice de la línea activa mediante búsqueda binaria, incluso tras un salto hacia atrás. Si el LRC carece de texto sincronizable, se usa la letra plana disponible. El resultado también distingue pista instrumental, letra no disponible y servicio temporalmente indisponible.
- **Verificación.** Compilación con 0 errores y 0 advertencias. Pasaron 84 pruebas xUnit, incluidas 18 nuevas sobre las marcas, el ajuste de tiempo, los saltos de posición y los estados de letra.
- **Pendiente para LRCLIB.** Caché local, vista WPF sincronizada con el reloj de reproducción y salto de audio. La validación del resaltado durante la reproducción queda para la integración de WPF.

## LRCLIB, tarea 4 — Caché local (2026-11-20)

- **Antes.** Una consulta válida dependía de que LRCLIB respondiera, incluso si la misma letra ya se había obtenido.
- **Qué se hizo.** Se añadió un contrato de caché y un proveedor que consulta los archivos JSON de `%LocalAppData%\MusicLayer\LyricsCache` antes de acceder a LRCLIB. La clave usa título, artista, álbum y duración disponibles. Los nombres de archivo son hashes de esa clave. Cada escritura sustituye el archivo completo tras terminar la serialización.
- **Qué cambió.** Una consulta con los mismos metadatos y duración recupera una letra confirmada o el estado instrumental sin conectarse a LRCLIB, incluso tras reiniciar. Los resultados sin coincidencia duran diez minutos en memoria. Un fallo temporal, una cancelación o un archivo de caché ilegible permite volver a consultar.
- **Verificación.** Compilación con 0 errores y 0 advertencias. Pasaron 91 pruebas xUnit, incluidas siete nuevas sobre recuperación desde otra instancia, aislamiento por versión, consultas simultáneas, archivos dañados, expiración y cancelación.
- **Pendiente para LRCLIB.** Vista WPF con cancelación por cambio de canción, resaltado sincronizado y salto de audio. La recuperación sin conexión desde la interfaz se probará con la vista integrada.

## LRCLIB, tarea 5 — Letras en WPF (2026-11-20)

- **Antes.** El cliente, el parser y la caché ya podían recuperar una letra. El reproductor aún carecía de una pantalla para leerla.
- **Qué se hizo.** Se incorporó la vista **Letras** siguiendo el panel de lectura, la línea verde y la tarjeta de pista de la pantalla 04 de `Guide/`. El ViewModel consulta el proveedor al abrir la vista para la pista activa y usa la posición que el reproductor ya comunica a la barra de progreso.
- **Qué cambió.** La pausa conserva la línea y, al reanudar, el resaltado sigue el audio. El lector permite desplazarse manualmente y volver con **Seguir línea actual**. Al cambiar de pista o salir de la vista se cancela la consulta anterior. El texto plano, las pistas instrumentales, la falta de coincidencia y los fallos temporales tienen presentación propia.
- **Verificación.** Compilación con 0 errores y 0 advertencias. Pasaron 96 pruebas xUnit. En una ventana WPF con un FLAC de prueba silenciado se verificaron la navegación, la recuperación desde JSON sin acceso al proveedor remoto, el resaltado, el desplazamiento manual, el retorno a la línea activa y la limpieza al detener.
- **Pendiente.** Validar de oído la sincronía con una canción conocida y una consulta real a LRCLIB. La tarea 6 habilitará adelantar y retroceder el audio desde la barra de progreso.
