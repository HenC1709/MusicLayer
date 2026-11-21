# MusicLayer

Reproductor de música de escritorio para Windows, desarrollado en C# con WPF. El proyecto seguirá Clean Architecture y usará SQLite para guardar datos locales.

## Alcance del MVP

- Cargar una carpeta y construir una biblioteca con archivos MP3, WAV y FLAC.
- Reproducir, pausar y detener canciones; navegar por la cola.
- Mostrar el nombre del archivo actual y ofrecer controles de volumen y progreso.
- Mostrar errores de lectura, archivos dañados, carpetas vacías y formatos no admitidos.

MusicLayer lee el título, artista y álbum incrustados en archivos MP3, WAV y FLAC y muestra la carátula incrustada de los FLAC. La persistencia SQLite forma parte del Sprint 4. Las playlists, el modo aleatorio, repeat, el ecualizador y una versión móvil quedan para después del MVP.

## Estructura

| Proyecto | Responsabilidad |
| --- | --- |
| `MusicLayer.Domain` | Entidades y reglas de la biblioteca y reproducción. |
| `MusicLayer.Application` | Casos de uso y contratos entre capas. |
| `MusicLayer.Infrastructure` | Acceso a archivos, audio, metadatos y persistencia SQLite. |
| `MusicLayer.Presentation.Wpf` | Aplicación de escritorio y presentación WPF. |
| `MusicLayer.Domain.Tests` | Pruebas de reglas de dominio. |
| `MusicLayer.Application.Tests` | Pruebas de casos de uso. |
| `MusicLayer.Infrastructure.Tests` | Pruebas de escaneo de archivos. |
| `MusicLayer.Presentation.Tests` | Pruebas del ViewModel. |

## Plan de trabajo

1. **Sprint 1 — Base y primer sonido:** solución, capas, contratos, escaneo de carpetas y reproducción inicial.
2. **Sprint 2 — Control de reproducción:** cola, pausa, siguiente/anterior, volumen, liberación de recursos y pruebas.
3. **Sprint 3 — Interfaz y MVP:** ventana WPF, controles, progreso, trabajo fuera del hilo de UI y errores visibles.
4. **Sprint 4 — Pulido:** metadatos, carátulas, persistencia completa, README con capturas y revisión final.

El detalle de cada sprint se acordará en `docs/SPRINTS.md`. Para el MVP, la biblioteca se mantendrá en memoria tras escanear la carpeta. SQLite se implementará en el Sprint 4 para preferencias locales; el alcance de la persistencia de la biblioteca se decidirá entonces.

## Decisiones iniciales

- Aplicación de escritorio Windows con WPF y .NET 10.
- NAudio reproduce el audio. TagLibSharp lee las etiquetas de texto de los formatos compatibles y las carátulas FLAC.
- La futura base SQLite vivirá en el directorio de datos local de la aplicación, fuera del repositorio.
- Domain y Application no dependerán de WPF, SQLite ni de la implementación concreta del audio.

## Empezar

Abre `MusicLayer.sln` en Visual Studio con la carga de trabajo de .NET Desktop instalada, o ejecuta `dotnet build MusicLayer.sln` con el SDK de .NET 10. La primera restauración descarga las dependencias desde NuGet.

Ejecuta `dotnet run --project src/MusicLayer.Presentation.Wpf` para abrir la ventana. En **Canciones**, pulsa **Importar carpeta**. Selecciona una fila y pulsa **Reproducir selección**, haz doble clic o presiona **Enter** sobre la fila. El fondo cyan identifica la selección. El punto verde identifica la pista que está sonando o en pausa.

Con el reproductor detenido, selecciona un FLAC para ver su carátula en el panel. Durante la reproducción y la pausa, el panel y la barra inferior muestran la portada de la pista activa aunque selecciones otra fila. Si el FLAC carece de imagen o la imagen es ilegible, aparece la ilustración de MusicLayer.

Abre **Ahora suena** para ver la pista activa y su cola. El panel muestra título, artista y álbum si están disponibles. Si falta el título, MusicLayer muestra el nombre del archivo. Todas las vistas comparten la barra inferior. Su botón central permite reproducir, pausar y reanudar desde la misma posición. **Reproducir selección** inicia el archivo elegido desde el principio.

Abre **Letras** mientras suena una canción o está en pausa. La línea activa aparece en verde y, si Windows tiene las animaciones habilitadas, se desplaza suavemente al entrar. Toca o haz clic en un verso sincronizado para saltar a su comienzo. Si la canción está en pausa, puedes elegir el verso y pulsar **Reanudar** cuando quieras escucharlo. Puedes desplazarte para leer otra parte y pulsar **Seguir línea actual** para volver. Si la letra solo tiene texto, se lee con desplazamiento manual. La vista también indica cuándo una pista es instrumental, cuándo falta la letra y cuándo el servicio está temporalmente indisponible.

MusicLayer busca letras únicamente mientras **Letras** está abierta. Primero revisa la caché en `%LocalAppData%\MusicLayer\LyricsCache`. Si necesita consultar LRCLIB, usa el título y artista de las etiquetas, junto con el álbum y la duración disponibles. Una letra ya guardada para esa versión se puede leer sin conexión. Al cambiar de canción o de vista, la consulta pendiente se cancela.

Usa **Anterior** y **Siguiente** para recorrer la cola en el orden de la lista. Antes de la primera reproducción, **Siguiente** inicia la primera canción. Al llegar a un extremo, la dirección que ya no tiene destino se deshabilita.

**Detener** corta el audio y libera el archivo, incluso cuando está en pausa. La cola conserva la posición para seguir navegando con **Anterior** y **Siguiente**.

Usa **Volumen** para ajustar el sonido de 0 a 100 antes o durante la reproducción. **0** silencia el audio sin detenerlo. El ajuste se mantiene al pausar, reanudar, detener o cambiar de canción. Al volver a abrir MusicLayer, el deslizador empieza en 100. Guardar esta preferencia queda para el Sprint 4.

Haz clic en la **barra de progreso** o arrastra el control y suéltalo para adelantar o retroceder. También puedes darle foco con **Tab** y usar las flechas. El verso activo se actualiza al aplicar el salto. Si saltas en pausa, pulsa **Reanudar** para continuar desde el nuevo punto. La barra vuelve a cero al detener o finalizar.

El escaneo incluye subcarpetas, omite las carpetas sin permisos de acceso y evita enlaces de sistema de archivos. La decodificación usa los códecs de Media Foundation de Windows.

Al importar una carpeta, MusicLayer muestra cuántas canciones encontró. Si la carpeta está vacía o solo contiene otros formatos, **Elegir carpeta** permite probar con otra. Si falla la lectura de la carpeta elegida, la biblioteca anterior sigue disponible. Ante un error de reproducción, **Elegir otra canción** devuelve el foco a la lista para seleccionar una pista con el teclado o el ratón.

Ejecuta `dotnet test MusicLayer.sln` para las pruebas xUnit de dominio, casos de uso, escaneo y estado del ViewModel. La suite actual incluye pruebas de integración con Infrastructure y WPF y requiere Windows. Consulta `docs/SPRINTS.md` para la validación manual del audio,
