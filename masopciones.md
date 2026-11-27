# Contexto y Pendiente UI: Menú de "Más opciones" en `NowPlayingView`

## 1. Cambios Realizados (2026-11-26)
Se activó el botón de menú secundario (`x:Name="MoreActions"`, ícono `&#xE712;`) en la barra de acciones de la vista de **Reproducción activa** (`NowPlayingView.xaml`), el cual anteriormente estaba deshabilitado como placeholder.

### Archivos modificados
1. **`src/MusicLayer.Presentation.Wpf/Views/NowPlayingView.xaml`**
   - Se habilitó el botón `MoreActions` y se conectó al evento `Click="OpenMoreOptions"`.
   - Se actualizó el `ToolTip` a `"Más opciones"`.
   - Se agregó un `<Button.ContextMenu>` nativo con las siguientes acciones:
     - `Abrir ubicación del archivo` (`Click="OpenFileLocation"`)
     - `Copiar ruta del archivo` (`Click="CopyFilePath"`)
     - `<Separator />`
     - `Abrir letras (.lrc)` (`Click="OpenLyrics"`)

2. **`src/MusicLayer.Presentation.Wpf/Views/NowPlayingView.xaml.cs`**
   - `OpenMoreOptions`: Abre el `ContextMenu` con clic izquierdo posicionándolo respecto al botón (`PlacementTarget = button`).
   - `OpenFileLocation`: Abre el Explorador de Windows seleccionando el archivo de audio actual (`explorer.exe /select,"{path}"`) usando `Model?.CoverSong?.FilePath`.
   - `CopyFilePath`: Copia `Model?.CoverSong?.FilePath` al portapapeles (`Clipboard.SetText`).

---

## 2. Tarea Pendiente para Mañana (Antes de compilar el `.exe`)
El `ContextMenu` actualmente usa el estilo por defecto blanco/claro de Windows, lo cual rompe con la estética oscura/esmeralda de la aplicación.

### Objetivo de Diseño
Estilizar `ContextMenu`, `MenuItem` y `Separator` dentro de los diccionarios de recursos del proyecto (`PlayerTheme.xaml` o `LibraryTheme.xaml`) para que hereden la paleta de colores de **MusicLayer**:
- **Fondo del menú:** Tono oscuro profundo acorde al panel (`#0A1D27` / `#102D36` o `RaisedBrush`).
- **Bordes:** Esquinas redondeadas (`CornerRadius="8"` o `"10"`), borde sutil `1px` (`#294D55` / `#385E66`).
- **Texto e íconos:** Blanco/claro para ítems activos, tono apagado (`Muted`) para estados deshabilitados.
- **Hover / Selección (`IsHighlighted`):** Resaltado sutil con acento menta (`{StaticResource Mint}`) o fondo elevado (`#163D47`) con transición suave.
- **Separador (`Separator`):** Línea fina de `1px` usando el color de borde (`#294D55`), sin márgenes excesivos.

### Opcional (Pulido UX)
- Deshabilitar automáticamente `MoreActions` (o sus ítems de archivo) cuando `Model?.CoverSong` sea `null`.
- Agregar glifos `Segoe MDL2 Assets` a la izquierda de cada `MenuItem` (carpeta, copiar, letra) para mantener coherencia visual con el botón `Letras` (`&#xE8A4;`).

---

## 3. Siguiente Paso: Build de Release (`.exe`)
Una vez aplicado el tema oscuro al `ContextMenu` y verificado visualmente en ejecución:
1. Verificar `dotnet build -c Release`.
2. Publicar el ejecutable standalone actualizado (`dotnet publish`) para distribución/pruebas.