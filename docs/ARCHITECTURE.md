# Arquitectura inicial

## Flujo de dependencias

```text
Presentation.Wpf → Application → Domain
Infrastructure → Application + Domain
Presentation.Wpf → Infrastructure (composition root)
```

Domain contiene reglas independientes del framework. Application organiza los casos de uso y define los contratos. Infrastructure implementa el audio, el escaneo de archivos y la lectura de etiquetas con TagLibSharp. El escáner lee las etiquetas de texto mediante `ITrackMetadataReader` fuera del hilo de interfaz y conserva los metadatos disponibles en cada `Song`. App.xaml.cs registra los servicios en Microsoft.Extensions.DependencyInjection y crea la ventana. MainViewModel recibe la carátula FLAC a través de ICoverArtReader y la decodifica fuera del hilo de interfaz. La persistencia SQLite forma parte del Sprint 4.

## SQLite local

La base SQLite se almacenará bajo el directorio de datos local del usuario en una carpeta `MusicLayer`; no se guardará dentro del repositorio ni junto al ejecutable.

SQLite se implementará en el Sprint 4. El esquema se definirá entonces para guardar preferencias como la última carpeta y el volumen; se decidirá si también conviene persistir la biblioteca. Durante el MVP la biblioteca se mantendrá en memoria. Las entidades de dominio no representarán directamente filas de la base de datos.

## Audio y concurrencia

La reproducción se inicia fuera del hilo de WPF. PlaybackEnded comunica finalización o error sin exponer tipos de NAudio. El reproductor libera salida y lector antes de notificar; los callbacks de salidas anteriores se ignoran. El ViewModel publica los cambios de estado en el contexto de UI y cancela y espera su operación pendiente antes de liberar su CancellationTokenSource. El contenedor dispone el reproductor al cerrar y la ventana captura errores de limpieza.
