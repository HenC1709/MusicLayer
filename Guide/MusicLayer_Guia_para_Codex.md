# MusicLayer — Guía de diseño e implementación para Codex

## Figma
Archivo editable: https://www.figma.com/design/vdv8kaq95uARI84GUR2sGx

**Objetivo**: experiencia final de un reproductor musical local de Windows en C#/.NET 10 con WPF. Los siete frames de Figma son la especificación visual, **no** un requisito de implementar todas sus funciones inmediatamente. Respetar el roadmap y los PR pequeños.

## Paleta visual obligatoria

| Token | Valor | Uso |
|---|---|---|
| Fondo base | `#0B1113` | Ventana principal negra-carbón |
| Tono casi negro | `#080D0F` | Áreas de contraste |
| Panel | `#111C1F` | Menú lateral y barra inferior |
| Panel elevado | `#14252A` | Tarjetas y formularios |
| Cyan profundo | `#123C44` | Contenedor de biblioteca / letras |
| Cyan intermedio | `#1B5D68` | Filas alternas |
| Cyan seleccionado | `#277E89` | Canción seleccionada |
| Verde esmeralda | `#39B779` | Progreso, selección, estados activos |
| Verde oscuro | `#163C2D` | Hover, estados de controles |
| Negro de controles | `#060C0E` | Fondo de botones |
| Texto primario | `#F4F9F8` | Títulos, iconos y botones |
| Texto secundario | `#A0B4B6` | Artistas y descripciones |
| Texto terciario | `#678287` | Datos poco destacados |

La interfaz debe tener alto contraste, jerarquía clara y estados de hover/focus/disabled. Los botones negros usan iconos blancos o grises con feedback esmeralda/cyan. No pintar el contenido general verde: el acento verde se reserva para acciones y estados.

## Frames de Figma (1440 x 900)

1. `01 · Biblioteca / Canciones` — `2:110`: listado cyan, navegación lateral, búsqueda, importar carpeta, carátula, reproductor fijo inferior.
2. `02 · Reproducción activa` — `4:76`: carátula grande, título/artista, cola con siguiente/anterior, controles persistentes.
3. `03 · Playlists` — `4:230`: cuadrícula de playlists, crear playlist, biblioteca local.
4. `04 · Letras` — `4:408`: lectura de letra con línea destacada en verde, desplazamiento y área informativa. Las líneas visibles son texto original de demostración.
5. `05 · Configuración` — `4:544`: carpeta predeterminada, volumen, continuidad de sesión, tema, preferencia de letras.
6. `06 · Menú hamburguesa expandido` — `4:684`: navegación a biblioteca, listas, letras, importar música, ajustes y más.
7. `07 · Estados vacíos + errores` — `4:840`: biblioteca sin canciones, letra no disponible, archivo ilegible y confirmación de importación.

Los nombres de artistas, canciones, álbumes y cifras mostradas son **datos de ejemplo**, no valores que deban fijarse en el código ni canciones del usuario.

## Reglas para Codex

- Revisar el proyecto original `HenC1709/MusicLayer`, documentos `docs/MusicLayerPlan.docx`, `docs/SPRINTS.md`, `docs/ARCHITECTURE.md` y rama de trabajo actual antes de modificar archivos.
- Conservar Domain / Application / Infrastructure / Presentation.Wpf y la inyección de dependencias. Aplicar MVVM y bindings; evitar mover lógica de audio a code-behind.
- WPF/XAML, **no** React, Electron, HTML/CSS, navegador ni Spotify clonado.
- Los siete frames describen el aspecto final, pero los sprints marcan el orden de desarrollo. No implementar persistencia, letras, playlists o ecualizador hasta la fase autorizada.
- Implementar una pieza por PR (aproximadamente 3–6 archivos cuando sea razonable); indicar archivos modificados, motivo, resultados reales de build/tests y pruebas manuales de Windows.
- Para barra inferior, priorizar reproducción accesible desde todas las vistas; al cambiar de pantalla no interrumpir la canción.
- Los controles visibles deben reflejar estados reales: play/pausa, progreso, volumen, carga, vacío, error, deshabilitado.
- Para letras: mostrar texto si hay fuente local/autorizada; si no, estado «Letra no disponible». No inventar letras ni asumir un servicio de terceros.
- No añadir servicios externos ni APIs para reproducir música local sin aprobación.

## Primer encargo recomendado a Codex

Analiza los diseños y la arquitectura actual. Compara el Sprint 2 del roadmap con el código existente. Propón una única tarea pequeña y un PR para ella. Detalla el comportamiento, las clases afectadas, pruebas xUnit y pruebas manuales. **No escribas código hasta que apruebe la propuesta**. Usa los frames solamente como referencia visual para la fase correspondiente, sin crear características adelantadas.

## Uso de los PNG

Exportar de Figma todos los siete frames a PNG a resolución 1x o 2x (seleccionar frame -> Export). Adjuntar las imágenes a Codex además de compartir el enlace Figma. Se diseñaron como frames editables independientes.
