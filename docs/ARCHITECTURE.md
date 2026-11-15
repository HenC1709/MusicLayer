# Arquitectura inicial

## Flujo de dependencias

```text
Presentation.Wpf → Application → Domain
Infrastructure → Application + Domain
Presentation.Wpf → Infrastructure (composition root)
```

Domain contiene reglas independientes del framework. Application organiza los casos de uso y define los contratos. Infrastructure implementa audio y sistema de archivos. App.xaml.cs registra los servicios en Microsoft.Extensions.DependencyInjection y crea la ventana. MainViewModel depende de IAudioPlayer y los casos de uso; la ventana se limita a bindings, selección de carpeta y cierre. Metadatos y SQLite se añadirán en sprints posteriores.

## SQLite local

La base SQLite se almacenará bajo el directorio de datos local del usuario en una carpeta `MusicLayer`; no se guardará dentro del repositorio ni junto al ejecutable.

SQLite se implementará en el Sprint 4. El esquema se definirá entonces para guardar preferencias como la última carpeta y el volumen; se decidirá si también conviene persistir la biblioteca. Durante el MVP la biblioteca se mantendrá en memoria. Las entidades de dominio no representarán directamente filas de la base de datos.

## Audio y concurrencia

La reproducción se inicia fuera del hilo de WPF. PlaybackEnded comunica finalización o error sin exponer tipos de NAudio. El reproductor libera salida y lector antes de notificar; los callbacks de salidas anteriores se ignoran. El ViewModel publica los cambios de estado en el contexto de UI y cancela y espera su operación pendiente antes de liberar su CancellationTokenSource. El contenedor dispone el reproductor al cerrar y la ventana captura errores de limpieza.
