# Plan de sprints

Este archivo sirve para acordar el trabajo antes de implementar cada sprint. El plan original está en `MusicLayerPlan.docx`.

## MVP 0 — Base compilable

- Objetivo: compilar la solución completa con `dotnet build`.
- Resultado: estructura de capas y punto de entrada mínimo de WPF.
- Pendiente para sprints posteriores: reproducción, interfaz, pruebas ejecutables y persistencia.

## Sprint 1 — Base y primer sonido

- Objetivo: elegir una carpeta, listar canciones y reproducir una selección.
- Alcance: entidad Song, contratos de escaneo y audio, casos de uso ScanMusicFolder y PlaySong, implementaciones locales y ventana de prueba WPF.
- Decisiones: escaneo recursivo sin seguir enlaces, biblioteca en memoria, nombre de archivo como título. NAudio 2.2.1 utiliza Media Foundation para decodificar; los formatos disponibles dependen de los códecs de Windows.
- Ciclo de vida: una instancia del reproductor por ventana, acceso serializado y liberación del audio al cambiar de canción o cerrar. La cola y la sesión de reproducción se abordarán en el Sprint 2.
- Contrato de audio: PlayAsync inicia un archivo, PlaybackEnded notifica finalización o error y DisposeAsync libera los recursos. Pausa, reanudación, volumen y posición se definirán en el Sprint 2.
- Criterios de aceptación: build correcto; elegir una carpeta y ver sus archivos MP3, WAV y FLAC; escuchar la selección; cambiar de archivo; cerrar y liberar audio; ver mensajes para carpeta vacía o archivo ilegible.
- Estado: implementación inicial; pendiente validar manualmente audio e interacción en Windows.

### Validación manual

1. Ejecutar `dotnet run --project src/MusicLayer.Presentation.Wpf`.
2. Elegir una carpeta con música, seleccionar un archivo y pulsar Reproducir seleccionada.
3. Probar MP3, WAV y FLAC; comprobar el sonido y la respuesta de la ventana.
4. Elegir otra canción mientras suena la primera y comprobar que solo suena la nueva.
5. Probar una carpeta vacía y un archivo de audio corrupto; comprobar los mensajes.
6. Cerrar durante reproducción y comprobar que el audio se detiene.

## Plantilla para cada sprint

### Sprint

- Objetivo:
- Alcance:
- Tareas:
- Dependencias y decisiones:
- Criterios de aceptación:
- Resultado y observaciones:

## Dirección inicial

El plan original propone primero escaneo y reproducción, después controles y cola, luego interfaz WPF y finalmente metadatos y persistencia. Los detalles se definirán antes de iniciar cada sprint. Para el MVP se mostrará el nombre del archivo; SQLite se abordará en el Sprint 4.
