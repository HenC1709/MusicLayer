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

## Sprint 2 — Control de reproducción

### PR 1 — Cola de reproducción

- La cola de Domain conserva el orden recibido y admite canciones duplicadas en índices distintos. Al cargar o reemplazar la lista, copia las canciones, expone una vista de solo lectura y reinicia la selección. Una lista vacía limpia la cola.
- La selección explícita usa un índice desde cero. Siguiente desde una cola sin selección elige la primera canción. Anterior desde una cola sin selección, y cualquier movimiento sin destino válido, devuelve `false` y conserva la posición. La navegación no es circular.
- Una colección nula, una canción nula o un índice inválido producen una excepción y conservan el estado anterior.
- Las pruebas xUnit de Domain cubren la navegación, sus extremos y la protección del estado. La sesión que conservará la cola entre acciones y la conexión con el reproductor corresponden a un PR posterior.

### PR 2 — Sesión de reproducción

- Application conserva una cola en memoria y reutiliza `PlaySong` para iniciar una canción por índice, la siguiente o la anterior. Cada acción devuelve la canción iniciada o `null` si no hay una posición a la que navegar.
- La sesión procesa las operaciones de una en una. Guarda la nueva posición solo cuando el reproductor confirma el inicio. Si el inicio falla o se cancela, conserva la posición previa para permitir un reintento.
- Reemplazar la cola reinicia la selección sin iniciar o detener el audio. La integración con la ventana, el avance automático y los demás controles quedan para cambios posteriores.

### Paso 3 — Navegación en la ventana

- La ventana conserva una sesión durante su ciclo de vida. Al cargar una carpeta reemplaza la cola y los botones Anterior y Siguiente navegan desde la última posición reproducida.
- Validación manual pendiente en Windows con audio real. Cargar una carpeta, iniciar una canción intermedia y comprobar que los botones cambian la canción y la selección visible. En ambos extremos comprobar que se conserva la posición.

### Pausa y reanudación

- Pausar conserva el archivo abierto, su posición y el índice de la cola. El botón está disponible mientras suena una canción y se deshabilita al pausar o finalizar.
- Reanudar usa la salida pausada y continúa desde la misma posición. El botón solo se habilita mientras el audio está en pausa.
- Validación manual en Windows. Reproducir una canción, pulsar Pausar y comprobar que el audio se interrumpe y se conserva la selección. Pulsar Reanudar y comprobar que continúa desde el punto de pausa. Probar Siguiente después de pausar y comprobar que se inicia la canción siguiente.

### Detener reproducción

- Detener libera el lector y la salida, deshabilita Pausar y Reanudar y conserva el índice de la cola. Volver a reproducir el archivo lo inicia desde el principio.
- Validación manual en Windows. Probar Detener mientras suena una canción y mientras está en pausa. Comprobar que el audio se corta, que el archivo se puede renombrar si ninguna otra aplicación lo usa y que la navegación parte del mismo índice.

### Volumen

- El deslizador ajusta el volumen de 0 a 100. MusicLayer aplica el valor al audio activo y lo conserva para la siguiente canción durante la sesión. Guardar el valor entre reinicios queda para el Sprint 4.
- Validación manual en Windows. Bajar el volumen antes de iniciar una canción. Con el audio en reproducción, mover el deslizador hasta 0 y volver a subirlo. Comprobar que el mismo nivel se mantiene tras pausar, reanudar y pasar a otra canción.

## Sprint 3 — Interfaz y MVP

### Tarea 1 — Progreso de reproducción

- Mostrar tiempo transcurrido y duración en la ventana. El indicador consulta la posición de la salida de audio, se congela al pausar y se restablece al detener, finalizar o cambiar de canción.
- Validación manual en Windows. Reproducir una canción conocida, comprobar los tiempos, pausar y esperar unos segundos, reanudar y luego pasar a un archivo de distinta duración. Al detener o dejar terminar la canción, comprobar que el tiempo vuelve a cero.

### Tarea 2 — Biblioteca WPF y reproductor persistente

- Las vistas **Canciones** y **Ahora suena** aplican los dos primeros diseños de `Guide/` a la biblioteca local y comparten una sesión y una barra de transporte. La ilustración es un recurso empaquetado con la aplicación.
- La selección permite elegir qué reproducir con Enter, doble clic o **Reproducir selección**. Una marca independiente señala la posición activa, incluso cuando hay entradas duplicadas. Al importar otra carpeta se reemplaza la cola y la barra sigue mostrando la pista anterior hasta que termina o se cambia.
- Para verificar en Windows, importar una carpeta con dos canciones, reproducir la primera y seleccionar la segunda con las flechas. Comprobar que el título y la marca siguen en la primera. Cambiar de vista, pausar, reanudar y detener desde la barra. Tras detener, la selección se conserva y se borra la marca.

### Tarea 3 — Estados vacíos, importación y errores

- La primera apertura invita a elegir una carpeta. Durante el escaneo se muestra la búsqueda de MP3, WAV y FLAC. Una importación termina con el número de canciones encontradas o con una indicación para elegir otra carpeta si no hay archivos compatibles.
- Si falla la lectura de la carpeta seleccionada, se conservan la lista y la selección anteriores. Durante un fallo de reproducción, el aviso permite volver a la biblioteca y elegir otra canción. El detalle técnico del error está disponible al pasar el puntero sobre el mensaje.
- Verificado en Windows con carpetas temporales, una carpeta ausente, un WAV dañado y otro válido. Se comprobó la recuperación desde **Ahora suena**, también con la ventana en su tamaño mínimo.

### Tarea 4 — Carátula FLAC · Completada

- TagLibSharp lee la portada frontal incrustada en el FLAC. Si no hay una imagen marcada como frontal, usa la primera imagen incrustada disponible.
- La vista previa sigue la selección cuando el reproductor está detenido. Al reproducir o pausar, ambos tamaños de portada siguen la pista activa. La lectura se realiza bajo demanda y una imagen ausente o ilegible deja visible la ilustración incluida con MusicLayer.

## Sprint 4 — Pulido y entrega

Dejar MusicLayer listo para portafolio con metadatos locales, preferencias que se recuperan al abrir la app y un README que permita ejecutar el proyecto desde un clon limpio.

### Tarea 1 — Título, artista y álbum

- Reutilizar TagLibSharp para leer las etiquetas de texto de MP3, WAV y FLAC. Mostrar el título en la biblioteca y los datos disponibles en **Ahora suena**.
- Criterio de aceptación. Un archivo etiquetado muestra sus datos. Si falta el título o falla la lectura de etiquetas, la canción se puede reproducir y la aplicación muestra el nombre del archivo.

### Tarea 2 — Carátulas en los demás formatos

- Extender el lector de carátulas a MP3 y a los WAV que incluyan imágenes incrustadas legibles por TagLibSharp.
- Criterio de aceptación. La carátula aparece al seleccionar o reproducir una pista. FLAC sigue funcionando y las pistas sin una imagen legible muestran la ilustración de MusicLayer.

### Tarea 3 — Repositorio de preferencias en SQLite

- Definir un contrato en Application e implementar en Infrastructure el almacenamiento de la última carpeta importada con éxito y del volumen. Crear la base bajo `%LocalAppData%\MusicLayer` al primer uso.
- Criterio de aceptación. Guardar y volver a leer las preferencias con una segunda instancia del repositorio. Comprobar que una base recién creada proporciona los valores iniciales.

### Tarea 4 — Restaurar las preferencias al abrir MusicLayer

- Conectar el repositorio al arranque y a las acciones de importar y ajustar volumen. Al abrir la aplicación, recuperar el volumen y volver a escanear la última carpeta guardada.
- Criterio de aceptación. Tras reiniciar, se recuperan la biblioteca y el volumen guardados. La reproducción queda detenida. Si la carpeta se movió, aparece un aviso y se puede elegir otra.

### Tarea 5 — Revisión final y README de portafolio

- Validar importación y reproducción de MP3, WAV y FLAC en Windows, junto con la recuperación de preferencias. Añadir capturas actuales, un diagrama de capas y las instrucciones de compilación y ejecución al README.
- Criterio de aceptación. Desde un clon limpio se puede compilar, ejecutar las pruebas y abrir MusicLayer siguiendo el README.

## Plantilla para cada sprint

### Sprint

- Objetivo:
- Alcance:
- Tareas:
- Dependencias y decisiones:
- Criterios de aceptación:
- Resultado y observaciones:

## Dirección inicial

El plan original propone primero escaneo y reproducción, después controles y cola, luego interfaz WPF y finalmente metadatos y persistencia. La biblioteca se construye en memoria a partir de la carpeta elegida. SQLite guardará las preferencias locales del Sprint 4.
