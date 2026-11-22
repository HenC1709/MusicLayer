# Tonic

Reproductor de música de escritorio para Windows, desarrollado en C# con WPF y organizado en capas. El repositorio de preferencias locales usa SQLite.

## Alcance del MVP

- Cargar una carpeta y construir una biblioteca con archivos MP3, WAV y FLAC.
- Reproducir, pausar y detener canciones; navegar por la cola.
- Mostrar el nombre del archivo actual y ofrecer controles de volumen y progreso.
- Mostrar errores de lectura, archivos dañados, carpetas vacías y formatos no admitidos.

Tonic lee el título, artista y álbum incrustados en archivos MP3, WAV y FLAC y muestra sus carátulas incrustadas compatibles. La persistencia SQLite forma parte del Sprint 4. Las playlists, el modo aleatorio, repeat, el ecualizador y una versión móvil quedan para después del MVP.

## Estructura

| Proyecto | Responsabilidad |
| --- | --- |
| `MusicLayer.Domain` | Entidades y reglas de la biblioteca y reproducción. |
| `MusicLayer.Application` | Casos de uso y contratos entre capas. |
| `MusicLayer.Infrastructure` | Acceso a archivos, audio, metadatos y persistencia SQLite. |
| `MusicLayer.Presentation.Wpf` | Aplicación de escritorio y presentación WPF. |
| `MusicLayer.Domain.Tests` | Pruebas de reglas de dominio. |
| `MusicLayer.Application.Tests` | Pruebas de casos de uso. |
| `MusicLayer.Infrastructure.Tests` | Pruebas de escaneo, audio, metadatos y persistencia local. |
| `MusicLayer.Presentation.Tests` | Pruebas del ViewModel. |

## Plan de trabajo

1. **Sprint 1 — Base y primer sonido:** solución, capas, contratos, escaneo de carpetas y reproducción inicial.
2. **Sprint 2 — Control de reproducción:** cola, pausa, siguiente/anterior, volumen, liberación de recursos y pruebas.
3. **Sprint 3 — Interfaz y MVP:** ventana WPF, controles, progreso, trabajo fuera del hilo de UI y errores visibles.
4. **Sprint 4 — Pulido y entrega.** Tonic muestra metadatos y carátulas de MP3, WAV y FLAC y guarda sus preferencias en SQLite. Queda la verificación final en Windows.

Tonic recuerda la última carpeta importada correctamente y el volumen. Al abrir, vuelve a escanear esa carpeta y espera a que inicies la reproducción. Si la carpeta se movió o perdió sus permisos, puedes elegir otra. El plan detallado se mantiene en el archivo local `docs/SPRINTS.md`.

### Refactor visual

La Biblioteca sigue la composición de la referencia 01 de Guide. La próxima pantalla es **Letras**. El [seguimiento del refactor visual](progress.md) recoge los archivos de referencia, las tareas restantes y sus criterios de aceptación.

## Decisiones iniciales

- Aplicación de escritorio Windows con WPF y .NET 10.
- NAudio reproduce el audio. TagLibSharp lee las etiquetas de texto de los formatos compatibles y sus carátulas incrustadas.
- El repositorio de preferencias crea `%LocalAppData%\MusicLayer\preferences.db` en su primera operación. Usa `Microsoft.Data.Sqlite` desde Infrastructure.
- Domain y Application no dependerán de WPF, SQLite ni de la implementación concreta del audio.

## Empezar

Abre `MusicLayer.sln` en Visual Studio con la carga de trabajo de .NET Desktop instalada, o ejecuta `dotnet build MusicLayer.sln` con el SDK de .NET 10. La primera restauración descarga las dependencias desde NuGet.

Ejecuta `dotnet run --project src/MusicLayer.Presentation.Wpf` y pulsa **Importar carpeta** en Biblioteca. Usa **Ctrl+F** para buscar por título, artista, álbum o género. Puedes ordenar la tabla, marcar favoritas o consultar las pistas reproducidas durante la sesión. Haz doble clic en una fila o pulsa **Enter** para escucharla. **Reproducir** inicia la primera pista visible y el botón aleatorio elige una de las pistas visibles. Siguiente y anterior recorren la cola original de la carpeta.

Con el reproductor detenido, selecciona un MP3, WAV o FLAC para ver su carátula en el panel. Tonic prefiere la imagen marcada como portada frontal. Si esa etiqueta falta, usa la primera imagen incrustada disponible. Durante la reproducción y la pausa, el panel y la barra inferior muestran la portada de la pista activa aunque selecciones otra fila. Si falta una portada legible, el panel de Biblioteca muestra el planeta del diseño de referencia.

En **Letras**, una cabecera compacta reúne la miniatura, el título y los datos disponibles de artista y álbum. Los versos ocupan todo el ancho del panel debajo de la cabecera.

Abre **Ahora suena** para ver la pista activa y su cola. El panel muestra título, artista y álbum si están disponibles. Si falta el título, Tonic muestra el nombre del archivo. Todas las vistas comparten la barra inferior. Su botón central permite reproducir, pausar y reanudar desde la misma posición. **Reproducir selección** inicia el archivo elegido desde el principio.

Abre **Letras** mientras suena una canción o está en pausa. Una barra esmeralda marca el verso activo, que aparece en blanco. El verso anterior y el siguiente tienen un contraste intermedio. Si Windows tiene las animaciones habilitadas, el verso activo se desplaza suavemente al entrar. Toca o haz clic en un verso sincronizado para saltar a su comienzo. Si la canción está en pausa, puedes elegir el verso y pulsar **Reanudar** cuando quieras escucharlo. Puedes desplazarte para leer otra parte y pulsar **Seguir línea actual** para volver. Si la letra solo tiene texto, se lee con desplazamiento manual. La vista también indica cuándo una pista es instrumental, cuándo falta la letra y cuándo el servicio está temporalmente indisponible.

Para usar una letra propia, guarda el archivo `.lrc` en la misma carpeta y con el mismo nombre base que el audio. Por ejemplo, `Canción.flac` y `Canción.lrc`. Tonic lee ese archivo al abrir **Letras**, incluso si el audio carece de etiquetas.

Si falta una letra local válida, Tonic revisa la caché en `%LocalAppData%\MusicLayer\LyricsCache`. Si necesita consultar LRCLIB, usa el título y artista de las etiquetas, junto con el álbum y la duración disponibles. Una letra ya guardada para esa versión se puede leer sin conexión. Al cambiar de canción o de vista, la consulta pendiente se cancela.

Usa **Anterior** y **Siguiente** para recorrer la cola en el orden de la lista. Antes de la primera reproducción, **Siguiente** inicia la primera canción. Al llegar a un extremo, la dirección que ya no tiene destino se deshabilita.

**Detener** corta el audio y libera el archivo, incluso cuando está en pausa. La cola conserva la posición para seguir navegando con **Anterior** y **Siguiente**.

Usa **Volumen** para ajustar el sonido de 0 a 100 antes o durante la reproducción. **0** silencia el audio sin detenerlo. Tonic recupera el último volumen aplicado al volver a abrir. En el primer arranque, el volumen empieza en 100.

Haz clic en la **barra de progreso** o arrastra el control y suéltalo para adelantar o retroceder. También puedes darle foco con **Tab** y usar las flechas. El verso activo se actualiza al aplicar el salto. Si saltas en pausa, pulsa **Reanudar** para continuar desde el nuevo punto. La barra vuelve a cero al detener o finalizar.

El escaneo incluye subcarpetas, omite las carpetas sin permisos de acceso y evita enlaces de sistema de archivos. La decodificación usa los códecs de Media Foundation de Windows.

Al importar una carpeta, Tonic muestra cuántas canciones encontró. Si la carpeta está vacía o solo contiene otros formatos, **Elegir carpeta** permite probar con otra. Si falla la lectura de la carpeta elegida, la biblioteca anterior sigue disponible. Ante un error de reproducción, **Elegir otra canción** devuelve el foco a la lista para seleccionar una pista con el teclado o el ratón.

Ejecuta `dotnet test MusicLayer.sln` en Windows para las pruebas xUnit de dominio, aplicación, infraestructura y presentación. Para comprobar el audio, importa MP3, WAV y FLAC y prueba la reproducción, la pausa y los saltos de posición.
