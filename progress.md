# Relevo del refactor visual de Tonic

**Punto de partida · 22 de noviembre de 2026.** La Biblioteca ya tiene su propia vista WPF. La siguiente entrega visual es **Letras, tarea 1**. Trabajar una pantalla por PR y actualizar aquí el estado y las pruebas al cerrarla. La tarea 5 del MVP 4 (entrega y verificación en Windows) sigue su propio calendario.

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

## Próximos PR, en orden

| Tarea | Referencia local | Entrega y criterio de aceptación |
| --- | --- | --- |
| 1 · Composición de Letras | `Letra.png`, `MusicLayer_Lyrics_Preview.png` | Reorganizar la pantalla alrededor del verso y del bloque de pista/carátula con las proporciones del frame 04. Partir de `Views/LyricsView.xaml` y de su cabecera en `MainWindow.xaml`. La letra sincronizada, plana, instrumental y ausente debe seguir siendo legible tanto en escritorio amplio como en ventana mínima. |
| 2 · Movimiento y seguimiento | `MusicLayer_Lyrics_Prototype.html`, `Letra.png` | Afinar el desplazamiento suave entre versos. Ya existen resaltado, desplazamiento manual, retorno al verso actual y salto al pulsar una línea. Probar pausa, reanudación, saltos hacia atrás y cambio rápido de pista. El scroll manual debe mantener el control hasta pedir volver. Depende de 1. |
| 3 · Fondo de Letras | `MusicLayer_Lyrics_Preview.png`, `Letra.png` | Adaptar el ambiente a la carátula y transicionar una vez por cambio de pista. Verificar portada clara, oscura, ausente y corrupta, contraste del texto y ausencia de parpadeos al actualizar el progreso. Depende de 1. |
| 4 · Reproducción activa | `Reproduccion-activa.png` | Dar a portada, título y artista el espacio principal y a la cola un panel diferenciado. Reutilizar sesión y barra inferior. Cambiar de pista desde la cola, pausar y volver a Biblioteca debe conservar identidad y posición. Se puede trabajar tras Biblioteca. |
| 5 · Menú expandido | `MenuNavegacion.png` | Extender la barra lateral de `MainWindow.xaml` con panel expandido, cierre por Escape, retorno del foco y destino activo visible. Verificar teclado y reproducción continua al abrir/cerrar. Añadir los destinos nuevos al estar disponibles. |
| 6 · Estados y recuperación | `Estados.png` | Traducir importación, biblioteca vacía, archivo ilegible y letra ausente a estados con icono, mensaje y acción pertinente. Provocar cada estado con archivos locales, comprobar recuperación y que el pie siga accesible. Reutilizar `NoticeKind` y estados de Letras. |
| 7 · Playlists | `PlayList.png` | Acordar primero creación, edición y persistencia. Después construir cuadrícula, tarjetas, estado vacío y detalle. Aceptar al crear una lista, volver a abrirla y reproducir su contenido con el orden acordado. Incorporar el destino al menú. |
| 8 · Configuración | `Configuracion.png` | Construir la pantalla por grupos de preferencias. Carpeta y volumen ya se guardan en SQLite. Verificar ambos tras reiniciar. Acordar las opciones de tema, letras y continuidad de sesión antes de presentarlas como configurables. |

## Evidencia de esta entrega

Build en Windows con .NET 10, cero advertencias y cero errores. Pasaron 121 pruebas xUnit (15 Domain, 30 Application, 42 Infrastructure y 34 Presentation). La comprobación WPF cargó una biblioteca temporal de 12 pistas y renderizó el estado inicial y la reproducción en 1586 × 992, 1120 × 720 y 660 × 460. Verificó búsqueda, selección de la fila filtrada, pista activa y navegación entre Biblioteca, Reproducción y Letras. Las capturas locales de esta ejecución están en `obj/LibraryReferenceCheck/screens/`; un clon limpio debe generarlas abriendo la app.

## Verificación y cierre

- Compilar con `dotnet build MusicLayer.sln` y ejecutar `dotnet test MusicLayer.sln` en Windows con .NET 10. Abrir con `dotnet run --project src/MusicLayer.Presentation.Wpf`.
- Para cada PR, comparar capturas con su PNG en escritorio amplio, tamaño inicial (1120 × 720) y tamaño mínimo (660 × 460). Registrar los estados realmente probados y cualquier diferencia pendiente. Usar pistas de prueba o propias al publicar capturas.
- Antes de cerrar el MVP 4 queda probar desde un clon limpio, validar salida de audio física y DPI alto, y preparar capturas finales para el README. La tarea visual inmediata continúa siendo la composición de Letras.
