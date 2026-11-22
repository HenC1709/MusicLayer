# Relevo del refactor visual de Tonic

**Punto de partida · 22 de noviembre de 2026.** La Biblioteca ya tiene su propia vista WPF. La composición de Letras está implementada. Letras ya tiene composición, seguimiento automático y fondo por carátula. La siguiente entrega visual es **tarea 4 · Reproducción activa**. Trabajar una pantalla por PR y actualizar aquí el estado y las pruebas al cerrarla. La tarea 5 del MVP 4 (entrega y verificación en Windows) sigue su propio calendario.

## Cómo retomar

1. Revisar el estado de Git y abrir el PNG de la tarea en la carpeta local `Guide/`. La guía escrita es `MusicLayer_Guia_para_Codex.md`. Si el clon carece de las imágenes, pedir la carpeta original antes de juzgar la fidelidad del diseño.
2. Comparar la referencia con la ventana ejecutable. Identificar dónde se sitúan la navegación, el contenido principal, las acciones, la carátula y el reproductor. Comparar también proporciones, densidad de filas, tipografía, comportamiento al reducir la ventana y foco de teclado.
3. Construir controles WPF enlazados a datos reales. Reservar los recortes de las referencias para arte y fondos. Una función propuesta que necesite esquema SQLite, contratos o dependencias nuevos requiere acordar el alcance antes de habilitarla.

## Biblioteca · implementada

Referencia: `Guide/biblioteca.png`. La barra lateral contiene la marca y las tres vistas disponibles. La Biblioteca reúne búsqueda con Ctrl+F, importación, contadores, seis categorías, controles de orden y reproducción, tabla con miniaturas y ficha lateral. El pie de reproducción es común a las tres vistas. En ventanas estrechas se prioriza la tabla y, con poca altura, las categorías pasan a un selector.

- `src/MusicLayer.Presentation.Wpf/Views/LibraryView.xaml` y su code-behind contienen la composición, el foco y la carga de carátulas visibles. `Resources/LibraryTheme.xaml` reúne los estilos de la vista.
- `ViewModels/LibraryBrowser.cs` filtra y ordena. `ViewModels/MainViewModel.cs` enlaza cada resultado con su índice original de cola. La pista activa permanece en el panel mientras se explora otra fila. Favoritas y Recientes duran la sesión.
- `Views/MainWindow.xaml` contiene navegación y transporte. `Resources/Art/LibraryArtwork.png` aporta la carátula de respaldo y `LibraryAmbient.png` el fondo. Las etiquetas de TagLib aportan artista, álbum, género, duración y datos de audio cuando existen.
- Para comprobar regresiones: reproducir una pista, buscar otra, seleccionar la fila filtrada, abrir Letras y regresar. Comprobar canción activa, selección, posición y volumen. Probar también carpeta vacía y archivo sin portada.

## Letras · composición implementada (tarea 1)

La pista y la carátula ocupan la columna izquierda y los versos la derecha. En la ventana mínima, la ficha musical pasa encima de la letra. El verso activo aumenta de tamaño y conserva el marcador y el foco visible.

La solución compiló sin advertencias ni errores y pasaron 121 pruebas. Una comprobación WPF con audio de prueba, LRC local y proveedor simulado renderizó letra sincronizada, plana, instrumental y ausente a 1586 × 992, 1120 × 720 y 660 × 460. También comprobó el foco del verso, el salto con clic en pausa y el retorno al seguimiento tras desplazamiento manual. La simulación de la barra espaciadora no confirmó el salto. Capturas locales en `obj/LyricsCompositionCheck/`.

## Letras · movimiento y seguimiento implementados (tarea 2)

El verso activo se desplaza suavemente hacia el 38 % del área de lectura cuando hay espacio. Si Windows tiene las animaciones desactivadas, la posición se actualiza de inmediato. La rueda, el gesto táctil, la barra de desplazamiento y las teclas de navegación suspenden el seguimiento durante tres segundos desde la última interacción. La sincronización del marcado continúa y el desplazamiento retoma el verso actual automáticamente. Al cambiar de pista se cancela el movimiento anterior.

La solución compiló sin advertencias ni errores y pasaron 121 pruebas. La comprobación WPF verificó el avance gradual, la pausa y reanudación, el salto hacia atrás, la suspensión con rueda y tecla Fin, el regreso al verso activo y el cambio de pista con movimiento pendiente. Se comprobó que el verso activo queda visible en los tres tamaños de referencia. Capturas en `obj/LyricsMotionCheck/run2/`.

## Letras · fondo por carátula implementado (tarea 3)

El degradado toma un color de la carátula y lo oscurece para mantener el contraste. La transición dura 650 ms y se inicia al resolverse la lectura. Si la portada falta o está dañada, se usa el fondo cian de respaldo. La vista aplica el nuevo fondo de inmediato cuando Windows tiene las animaciones desactivadas.

La compilación terminó sin advertencias ni errores y pasaron 121 pruebas. La comprobación WPF usó cuatro pistas con portadas de prueba clara, oscura, corrupta y ausente. Verificó que el color se mantiene durante el avance de la pista, que vuelve al respaldo cuando corresponde y que la letra retoma sola el verso después de desplazarla con la rueda. Se capturaron los tres tamaños de referencia en `obj/LyricsAmbientCheck/run1/`.

## Letras · iluminación progresiva implementada (tarea 3.5)

El verso activo se ilumina palabra por palabra en verde menta a medida que avanza el reloj de reproducción. El ritmo se estima con las marcas de inicio de cada verso y la duración de la pista para el último. Al pausar se conserva el resaltado y al adelantar o retroceder se recalcula.

La solución compiló sin advertencias ni errores y pasaron 121 pruebas. La comprobación WPF verificó el avance durante la reproducción, la pausa, el retroceso y el cambio de verso. También verificó el contraste entre las palabras iluminadas y las pendientes en versos de varios renglones en los tres tamaños de referencia. Capturas en `obj/LyricsKaraokeCheck/run1/`.

## Próximos PR, en orden

| Tarea | Referencia local | Entrega y criterio de aceptación |
| --- | --- | --- |
| 4 · Reproducción activa | `Reproduccion-activa.png` | Dar a portada, título y artista el espacio principal y a la cola un panel diferenciado. Reutilizar sesión y barra inferior. Cambiar de pista desde la cola, pausar y volver a Biblioteca debe conservar identidad y posición. Se puede trabajar tras Biblioteca. |
| 5 · Menú expandido | `MenuNavegacion.png` | Extender la barra lateral de `MainWindow.xaml` con panel expandido, cierre por Escape, retorno del foco y destino activo visible. Verificar teclado y reproducción continua al abrir/cerrar. Añadir los destinos nuevos al estar disponibles. |
| 6 · Estados y recuperación | `Estados.png` | Traducir importación, biblioteca vacía, archivo ilegible y letra ausente a estados con icono, mensaje y acción pertinente. Provocar cada estado con archivos locales, comprobar recuperación y que el pie siga accesible. Reutilizar `NoticeKind` y estados de Letras. |
| 7 · Playlists | `PlayList.png` | Acordar primero creación, edición y persistencia. Después construir cuadrícula, tarjetas, estado vacío y detalle. Aceptar al crear una lista, volver a abrirla y reproducir su contenido con el orden acordado. Incorporar el destino al menú. |
| 8 · Configuración | `Configuracion.png` | Construir la pantalla por grupos de preferencias. Carpeta y volumen ya se guardan en SQLite. Verificar ambos tras reiniciar. Acordar las opciones de tema, letras y continuidad de sesión antes de presentarlas como configurables. |

## Evidencia de Biblioteca

Build en Windows con .NET 10, cero advertencias y cero errores. Pasaron 121 pruebas xUnit (15 Domain, 30 Application, 42 Infrastructure y 34 Presentation). La comprobación WPF cargó una biblioteca temporal de 12 pistas y renderizó el estado inicial y la reproducción en 1586 × 992, 1120 × 720 y 660 × 460. Verificó búsqueda, selección de la fila filtrada, pista activa y navegación entre Biblioteca, Reproducción y Letras. Las capturas locales de esta ejecución están en `obj/LibraryReferenceCheck/screens/`; un clon limpio debe generarlas abriendo la app.

## Verificación y cierre

- Compilar con `dotnet build MusicLayer.sln` y ejecutar `dotnet test MusicLayer.sln` en Windows con .NET 10. Abrir con `dotnet run --project src/MusicLayer.Presentation.Wpf`.
- Para cada PR, comparar capturas con su PNG en escritorio amplio, tamaño inicial (1120 × 720) y tamaño mínimo (660 × 460). Registrar los estados realmente probados y cualquier diferencia pendiente. Usar pistas de prueba o propias al publicar capturas.
- Antes de cerrar el MVP 4 queda probar desde un clon limpio, validar salida de audio física y DPI alto, y preparar capturas finales para el README. La siguiente tarea visual es Reproducción activa.
