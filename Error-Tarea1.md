# Post-Mortem y Resolución: Lag de Scroll en Tarea 1 (`error-tarea1`)

La variante activa para continuar con la Tarea 2 es la [versión 2](#versión-2--base-para-la-tarea-2). La primera parte conserva el historial de la prueba con el acelerador y la barra personalizada.

## 1. Resumen del Incidente
Durante la validación de la **Tarea 1 (Biblioteca e importación)** en Android, la lista de canciones presentaba tirones severos, pérdida de inercia al deslizar con un dedo y frenazos frecuentes, dando la impresión de que la lista se recargaba constantemente por bloques.

El problema no fue un error de ejecución de Codex, sino una **dependencia de planificación en el roadmap**:
* En la **Tarea 1** se especificó explícitamente *"Mantener el CollectionView con bindings compilados"* y *"Entregar APK firmada en Debug"*.
* La optimización real de rendimiento (*"Validar la caché de miniaturas y el desplazamiento con una biblioteca grande"* + compilación en Release) estaba diferida hasta la **Tarea 7**.
* Al seguir las instrucciones al pie de la letra, el agente mantuvo una arquitectura de lista pesada y un paquete sin optimización nativa AOT.

---

## 2. Causas Técnicas Raíz (Auditoría sobre .NET 10 MAUI Android)

1. **Compilación en Debug con intérprete Mono sin AOT (84.9 MB):**
   - El APK de prueba tenía `android:debuggable="true"` y `UseInterpreter=true` (valor por defecto de `Microsoft.Android.Sdk` en `Debug`), sin compilación Ahead-of-Time (AOT) ni trimming.
   - En cada movimiento de scroll, los cruces JNI (Java ↔ C#) pasaban por el intérprete y trampolines de depuración lentos, bloqueando el hilo de UI decenas de milisegundos y degradando el muestreo del `VelocityTracker` de Android (cortando el *fling* en seco).
2. **Sobrecarga de `CollectionView` envolviendo vistas nativas:**
   - Aunque se creó una celda nativa (`LibrarySongRowView`), al vivir dentro del `CollectionView` de MAUI se seguían ejecutando mutaciones del árbol lógico (`RemoveLogicalChild` / `AddLogicalChild`), cambios de `VisualStateManager`, envolturas `ItemContentView`, `SelectableViewHolder` (con `IsSelectionEnabled = true` y `SetOnClickListener` provocando retrasos táctiles y reseteos JNI en cada reciclaje) y eventos `OnScrolled` por frame.
   - Además, el `Grid` de MAUI pasaba `MeasureSpecMode.AtMost` a la lista, forzando dobles pasadas de auto-medición en el `RecyclerView`.
3. **Intercepción de `NestedScrolling`:**
   - El `CoordinatorLayout` / `AppBarLayout` raíz de MAUI interceptaba gestos de arrastre y *fling* innecesariamente porque `NestedScrollingEnabled` seguía activo en la lista.
4. **Cálculo síncrono de texto y accesibilidad en `Bind()`:**
   - `TextUtils.Ellipsize` medía glifos desde cero y se reasignaba `ContentDescription` en cada reciclaje de fila durante el desplazamiento (disparando notificaciones de accesibilidad por cada fila).
5. **Recreación del Adapter al filtrar:**
   - Al buscar en `RefreshResults()`, se reasignaba una nueva `ObservableCollection`, destruyendo y reconstruyendo todo el `Adapter` del `RecyclerView`.

---

## 3. Solución Implementada (Rama `maui`)

Se adelantó la optimización de lista y caché desde la Tarea 7 hacia la base de la Tarea 1:
* **Reemplazo de `CollectionView` por `LibraryTrackList` + `LibraryTrackListHandler`:** Control nativo directo sobre `AndroidX.RecyclerView` (`LibraryRecyclerView` + `LibraryTrackAdapter` + `LibrarySongRowView`), eliminando la capa intermedia de MAUI y los obsoletos `LibrarySongRow.cs` y `LibrarySongRowHandler.cs`.
* **Física de scroll, medición y *Fast-Scroll*:** `NestedScrollingEnabled = false`, `Parent?.RequestDisallowInterceptTouchEvent(true)` en `ACTION_DOWN`, conversión de `AtMost` a `Exactly` en `OnMeasure`, `HasFixedSize = true`, `SetItemAnimator(null)`, caché de vistas (`16`) y `RecycledViewPool` (`24`) con prefetch (`InitialPrefetchItemCount = 10`), impulso progresivo de inercia en `Fling(...)` y barra lateral de desplazamiento rápido (`FastScrollOverlay`).
* **Caché de carátulas en dos niveles (`CoverThumbnail` + `LibraryCoverCache`):** Miniaturas JPEG de `120 × 120 px` en disco y caché LRU en memoria ampliada de 128 a **256 bitmaps** pre-recortados y pre-redondeados con `BitmapShader` + `PrepareToDraw()`, con decodificación asíncrona limitada por semáforo (`DecodeGate`, máximo 2 concurrentes).
* **Caché de texto truncado y accesibilidad diferida:** Los textos truncados (`TextUtils.Ellipsize`) y el ancho del formato se cachean por pista y ancho de vista; `ContentDescription` se resuelve bajo demanda mediante `OnInitializeAccessibilityNodeInfo`.
* **Compilación Release con AOT y fix del SDK `.NET 10.0.300`:**
  - En `MusicLayer.Presentation.Maui.csproj` se subió la versión a `1.0.3` (`ApplicationVersion = 4`), se configuró `<AndroidPackageFormat Condition="'$(Configuration)' == 'Release'">apk</AndroidPackageFormat>` y se añadió `<UseDefaultPublishRuntimeIdentifier>false</UseDefaultPublishRuntimeIdentifier>` para evitar el error `NU1102` (`Microsoft.NETCore.App.Runtime.Mono.win-x64` inyectado por el SDK `10.0.300` al activar `PublishTrimmed` en `Release`).
  - `artifacts/Android/Tonic.apk` bajó de **84,9 MB** a **28,26 MB** con los **94 ensamblados** compilados a AOT nativo (`libaot-*.so` para ARM64 y x86-64), conservando exactamente la misma firma X.509 (`debug.keystore`) para actualizar sin perder las 118 canciones guardadas en `library.json`.
  - Validación: `Release` y `Debug` compilaron con **0 advertencias y 0 errores**, y pasaron las **138/138 pruebas** xUnit de la solución (`MusicLayer.sln`).
* **Configuración del entorno Java en VS Code para wrappers autogenerados (`obj/`):**
  - Se actualizaron `.vscode/settings.json` y `.vscode/jdt.prefs` (`java.project.sourcePaths`, `java.project.referencedLibraries`, salida aislada en `jdtls-classes` y exclusión de `Release`/`bin`) para resolver los falsos errores del servidor Java sobre los *Java Callable Wrappers* generados por Mono (como `LibrarySongRowView.java`); verificado con `javac` sobre los 367 `.java` generados (**0 errores**).

---

## 4. Ajustes Aplicados al Roadmap (Tareas 1 y 7)
Para evitar que futuros agentes reviertan esta optimización al leer el plan original:
* **Tarea 1:** Sustituir *"Mantener el CollectionView con bindings compilados"* por *"Mantener el control nativo `LibraryTrackList` (`RecyclerView`)"*.
* **Tarea 7:** Mantener la persistencia SQLite, restauración de sesión y validación de miniaturas de `300 × 300 px`, dando por resuelta la fluidez de desplazamiento de lista grande.

---

## 5. Reglas Operativas para Siguientes Tareas (Tareas 2 a 7)
1. **Revisar dependencias de rendimiento antes de ejecutar:** No diferir optimizaciones críticas de UI/compilación si afectan las pruebas diarias del APK.
2. **Prohibir comandos de Git al agente:** Mantener control manual total sobre `add`, `commit`, `checkout` y ramas.
3. **Prohibir compilación automática al agente:** Instruir al agente para que solo edite código fuente; ejecutar manualmente `dotnet build` / `dotnet publish` desde la terminal local para evitar cadenas repetitivas de permisos de firma.

## 6. pendientes 
* Lo que te queda pendiente, por si lo quieres anotar:

* Quitar el boost del Fling(...) y decidir si dejas el fast-scroll.
* Sacar el zip y el .vscode del repo.
* Actualizar la tarea 1 del md para que diga lista nativa en vez de CollectionView.
* Bajar la capacidad de la caché de portadas antes de pasar a miniaturas de 300 px.

# Versión 2 · Base para la Tarea 2

# Post-mortem: lag de scroll en la Tarea 1 (`error-tarea1`)

## 1. Resumen
Al validar la **Tarea 1 (Biblioteca e importación)** en Android, la lista tenía tirones, perdía la inercia con un dedo y se frenaba seguido.

No fue un fallo del agente. Fue una dependencia de planificación en el roadmap:
- La Tarea 1 pedía *"Mantener el CollectionView con bindings compilados"* y *"APK firmada en Debug"*.
- La validación de rendimiento (caché de miniaturas, desplazamiento con biblioteca grande, Release) estaba en la Tarea 7.
- El agente siguió el plan al pie de la letra: lista con `CollectionView` y APK sin optimizar.

## 2. Causas probables (no medidas por separado)
Se cambió todo a la vez (Release, `RecyclerView`, caché), así que **no se sabe cuál cambio arregló qué**. Probado a mano en el teléfono con 118 canciones: el scroll quedó fluido.

1. **APK en Debug sin AOT (84,9 MB):** `android:debuggable="true"`, sin compilación nativa. La APK Release con AOT quedó en 28,26 MiB. La prueba conjunta no permite ordenar las causas por impacto.
2. **`CollectionView` de MAUI sobre filas nativas:** agrega trabajo por fila reciclada (árbol lógico, `ItemContentView`, selección, `OnScrolled`).
3. **Trabajo en `Bind()`:** `TextUtils.Ellipsize` y `ContentDescription` en cada reciclaje de fila.
4. **`NestedScrolling` activo:** probablemente irrelevante, porque la app no usa `Shell` ni `NavigationPage`.

## 3. Solución aplicada (rama `maui`)
- `CollectionView` reemplazado por `LibraryTrackList` + `LibraryTrackListHandler` (`RecyclerView` nativo con `LibrarySongRowView` como fila).
- Texto truncado cacheado por pista y ancho; accesibilidad diferida con `OnInitializeAccessibilityNodeInfo`.
- Caché de carátulas LRU (256 miniaturas de 120 px ya recortadas) con decodificación limitada a 2 hilos (`DecodeGate`).
- **No se decodifican carátulas mientras la lista está en fling;** se cargan al detenerse.
- Compilación Release con AOT (`1.0.3`, `versionCode=4`), APK 28,26 MB, misma firma que la anterior.
- 138/138 pruebas pasando.

La variante activa usa la inercia y el indicador vertical estándar de Android. La espera de carátulas durante el desplazamiento inercial pertenece a cada lista y se libera al detenerla o desmontarla. Sigue pendiente generar y probar en Release una APK con este cambio. La APK 1.0.3 existente corresponde a la variante anterior.

## 4. Ajustes al roadmap
- **Tarea 1:** cambiar *"Mantener el CollectionView con bindings compilados"* por *"Lista nativa `LibraryTrackList` (`RecyclerView`)"*.
- **Tarea 7:** mantener la validación del scroll **con una biblioteca grande (1000+ canciones)** y de las miniaturas de 300 × 300 px. Con 118 canciones no está validado.

## 5. Reglas para las Tareas 2 a 7
1. Revisar dependencias de rendimiento antes de ejecutar; no diferir optimizaciones que afecten las pruebas diarias del APK.
2. El agente no usa comandos de Git (`add`, `commit`, `checkout`, ramas).
3. El agente solo edita código; `dotnet build` / `dotnet publish` se ejecutan a mano.

## 6. Pendientes
- [ ] Sacar de git `MusicLayer.Presentation.Maui.zip` y la configuración personal de `.vscode/` (`settings.json` con rutas de usuario, `jdt.prefs`).
- [ ] Bajar la capacidad de la caché de portadas antes de pasar a miniaturas de 300 px (256 × 300 px ≈ 90 MB).
- [ ] Confirmar `SupportedOSPlatformVersion = 33` (solo Android 13+).
- [ ] Validar el scroll con 1000+ canciones.
- [ ] Opcional: prueba A/B de Release con `CollectionView` contra Release con `RecyclerView`, para saber si el `RecyclerView` era necesario.
