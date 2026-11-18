# MusicLayer

Reproductor de música de escritorio para Windows, desarrollado en C# con WPF. El proyecto seguirá Clean Architecture y usará SQLite para guardar datos locales.

## Alcance del MVP

- Cargar una carpeta y construir una biblioteca con archivos MP3, WAV y FLAC.
- Reproducir, pausar y detener canciones; navegar por la cola.
- Mostrar el nombre del archivo actual y ofrecer controles de volumen y progreso.
- Mostrar errores de lectura, archivos dañados, carpetas vacías y formatos no admitidos.

Los metadatos, las carátulas y la persistencia SQLite quedan para el Sprint 4. Las playlists, el modo aleatorio, repeat, el ecualizador y una versión móvil quedan para después del MVP.

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
- Las dependencias de audio y etiquetas previstas son NAudio y TagLibSharp.
- La futura base SQLite vivirá en el directorio de datos local de la aplicación, fuera del repositorio.
- Domain y Application no dependerán de WPF, SQLite ni de la implementación concreta del audio.

## Empezar

Abre `MusicLayer.sln` en Visual Studio con la carga de trabajo de .NET Desktop instalada, o ejecuta `dotnet build MusicLayer.sln` con el SDK de .NET 10. La restauración requiere acceso a NuGet para descargar NAudio.

Ejecuta `dotnet run --project src/MusicLayer.Presentation.Wpf` para abrir la ventana. Elige una carpeta, selecciona un archivo y pulsa **Reproducir seleccionada**. Usa **Anterior** y **Siguiente** para recorrer la cola en el orden de la lista. Si todavía no has reproducido una canción de la cola, **Siguiente** inicia la primera. Al llegar a un extremo, la cola conserva su posición.

El escaneo incluye subcarpetas, omite las carpetas sin permisos de acceso y evita enlaces de sistema de archivos. La decodificación usa los códecs de Media Foundation de Windows. La pausa, la reanudación, la detención, el volumen y la barra de progreso siguen pendientes.

Ejecuta `dotnet test MusicLayer.sln` para las pruebas xUnit de dominio, casos de uso, escaneo y estado del ViewModel. La suite actual incluye pruebas de integración con Infrastructure y WPF y requiere Windows. Consulta `docs/SPRINTS.md` para la validación manual del audio.
