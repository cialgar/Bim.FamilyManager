# PROGRESS.md — Estado del proyecto

> Claude: leer este archivo COMPLETO al iniciar cada sesión. Actualizarlo al cerrar cada sesión.

## Estado actual

**Fase 1 CERRADA** (2026-07-18): criterio "0 bytes" verificado por el usuario — sesión B con
0 extracciones y 42 familias servidas desde caché, miniaturas y badges instantáneos.
**Fase activa: Fase 2** (índice SQLite) — núcleo puro **hecho y testeado** (35/35 tests):
`PartAtomReader` completo, `Bim.FamilyManager.Index` (schema + `IndexScanner` incremental +
`IndexQuery` FTS5). Falta `IndexedDirectorySource` (integración Revit), re-scan al abrir y
la prueba de estrés con la librería completa.
**Última sesión:** 2026-07-18 — cierre Fase 1, arranque Fase 2 (núcleo del índice).

## Próximos pasos inmediatos

1. Fase 2, tarea 3 — `IndexedDirectorySource`: implementación de `FamilySource<TOptions>`
   que consulta el índice en vez de escanear (jerarquía de carpetas desde la columna
   `folder`). Decidir en ARCHITECTURE si convive con `DirectorySource` o la reemplaza.
2. Fase 2, tarea 4 — comando "Reindexar" en settings + re-scan ligero al abrir Revit
   (`IndexScanner.Scan` ya es incremental: solo diff de mtimes si nada cambió).
3. Fase 2, tarea 5 — estrés con la librería real completa: medir indexación inicial,
   re-scan (< 10 s objetivo) y latencia de búsqueda (< 100 ms objetivo); registrar métricas.
4. Fase 3 — mapa de normalización de categorías ES/EN que rellene `category_key` (decisión
   registrada en ARCHITECTURE: el PartAtom no trae id estable; OmniClass solo en modelo).
5. Pendientes heredados: hueco Navigator; .rfa en raíz sin listar; PRs a scotec-revit
   (Category, InvariantCulture en `updated`, stream del loader sin disponer en Initialize).

## Fase 2 — estado de detalle

- `PartAtomReader` (Rfa, reemplaza a `PartAtomCategoryReader`): título, product-version,
  updated, categoría localizada, OmniClass y nombres de tipos en una pasada por sectores.
  Hallazgos: el product-version del PartAtom es autoritativo (fixture "v2025" realmente
  guardado en 2026); las familias de anotación no llevan OmniClass.
- `Bim.FamilyManager.Index` (puro: Microsoft.Data.Sqlite 8.0.11 + Rfa): `FamilyIndex`
  (schema WAL + FTS5, `%AppData%\FamilyManager\index.db`), `IndexScanner` (incremental,
  transaccional, progreso, tolerante a carpetas inaccesibles y archivos corruptos — se
  indexan con datos básicos y se reportan), `IndexQuery` (FTS por prefijo por token +
  filtros categoría/versión/carpeta/favoritos/tag + `GetCategories`). Tablas tags/favorites
  creadas; su API llega con la Fase 3.
- Tests: `Bim.FamilyManager.Index.Tests` reutiliza los fixtures de Rfa.Tests por link (sin
  duplicar binarios). 13 tests del índice + 22 de Rfa = 35/35 verdes. Cubren: alta inicial,
  re-scan sin cambios (0 lecturas), modificado→reindexado, borrado→eliminado, backups
  ignorados, búsqueda por nombre y **por nombre de tipo** (`"96"` → puerta), filtros, corrupto
  findable por nombre, `category_key` null hasta Fase 3.

## Git

- Remotes: `origin` = `https://github.com/cialgar/Bim.FamilyManager` (fork), `upstream` =
  scotec. Push verificado con `git ls-remote`: `develop` (espejo upstream, tag
  `upstream-base` en `e089d29`), `fix/empty-family-panel` (`2f1010f` logging + `0183033`
  DirectFamilies — 2 PRs upstream independientes en Fase 4) y `feature/rfa-cache` (rama
  actual, `cfe30bf`, basada en fix/empty-family-panel para conservar el fix del panel).
- CLAUDE.md, PROGRESS.md, docs/ y .claude/ siguen sin trackear a propósito; decidir su
  rama/destino más adelante (probablemente una rama fork propia, no las de PR).

## Fase 1 — estado de detalle

### Integración del caché (sesión 2026-07-18, tarde)

- `FamilyInfoCache` reemplaza a `ThumbnailCache`: entrada = `.png` (miniatura) + `.json`
  (Product/ProductVersion/Updated/HasThumbnail; se escribe último como marcador de entrada
  completa). Mismo esquema de claves e invalidación. 20 tests verdes.
- `RevitFamily.ApplyCachedInfo` (Base): inicializa la familia desde el caché sin leer el
  .rfa; los getters Product/ProductVersion/Updated caen al valor cacheado hasta que el
  `RevitFamilyInfo` real se inicialice (interacción); `ApplyUpdate` limpia lo cacheado.
- `DirectorySource`: hit → `ApplyCachedInfo` + log "served from cache"; miss → handler en
  `Initialized` guarda lo extraído + log "stored in the cache". `FamilyInfoCache` singleton
  registrado en el RegistrationModule de Source.Directory.
- Tarea 2 (lectura sin copiar) re-resuelta como decisión: ver ARCHITECTURE § Registro
  (el `Initialize()` de Scotec no dispone el stream del loader → FileStream ahí es peligroso;
  PR candidato). La lectura por sectores queda en nuestros lectores y el scanner de Fase 2.

### Base del proyecto (sesión 2026-07-18, mediodía)

- `Source/Bim.FamilyManager.Rfa`: librería pura (solo OpenMcdf + BCL; ni siquiera necesita
  Scotec.Revit). `ThumbnailCache` (clave `hashRuta-hashEstado.png`, escritura atómica,
  invalidación por mtime/size con borrado de entradas viejas, self-healing) y
  `PartAtomCategoryReader` (categoría del stream `PartAtom`, lee solo los sectores
  necesarios, tolerante a corruptos → null).
- `Source/Bim.FamilyManager.Rfa.Tests`: 18 tests xUnit verdes (62 ms) con 5 fixtures reales
  (2025/2026, categorías en inglés y español, uno sin preview, uno truncado). Paquetes de
  test agregados a `Directory.Packages.props`; ambos proyectos en el `.slnx`.
- Hallazgo de build documentado en CLAUDE.md: los BG1002/CS2001 intermitentes se deben a
  que los obj/ no separan por `RevitYear` — `dotnet test` (2025 default) mezcla estado con
  el build 2026; receta: limpiar todos los obj/bin.

## Medición de referencia (librería real, 2026-07-18)

Harness replicando el pipeline del panel sobre `Uploads LR` (191 familias, 145 MB, caché de
SO caliente): enumeración+creación 8+8 ms; `RevitFamilyInfo.Initialize()` 321 ms secuencial
(1.7 ms/familia) / 163 ms con DOP 4; previews en 190/191 (361 KB PNG total). Costo dominante:
el flujo upstream lee el .rfa COMPLETO a memoria y nada persiste entre sesiones — base del
re-scope de Fase 1.

## Bug del panel vacío — RESUELTO Y VERIFICADO (2026-07-18)

**Verificado por el usuario en Revit 2026:** con el fix `DirectFamilies` desplegado, el panel
muestra las familias de "familias exportadas" con miniaturas y badge de versión, y el drag &
drop coloca familias en el modelo. Con esto el smoke test de la Fase 0 queda cubierto
(panel + fuente + navegación + carga + colocación). Detalle original del diagnóstico abajo.

### Detalle del diagnóstico

**No es una excepción: es un hueco de diseño de la vista del upstream.** Con la
instrumentación desplegada, la repro del usuario no dejó ni un error en el log, las carpetas
renderizan y las familias no. Al leer `FamilyManagerView.xaml` (FamilyExplorer): el área
principal solo enlaza `SelectedFolder.Subfolders` — **las familias directas de la carpeta
seleccionada en el combo no tienen representación visual**. Solo se dibujan familias (a)
dentro del `FolderView` de una subcarpeta al expandir su chevron, o (b) en `SearchResult` al
buscar con ≥3 caracteres. Una carpeta sin subcarpetas (p. ej. "familias exportadas", 38 .rfa)
muestra vacío absoluto con cualquier archivo. `FamilyNavigator` tiene el hueco hermano
(`FamilyManagerViewModel` L248-252: si hay subcarpetas muestra SOLO subcarpetas; familias
solo si no hay ninguna).

**Fix aplicado (Explorer):** nueva propiedad `DirectFamilies` en la base
`FolderViewModel<T>` (familias con `includeSubfolders: false`, con guard + logging) y
sección nueva en `FamilyManagerView.xaml` que la renderiza encima de las subcarpetas.
Desplegado el 2026-07-18. **Verificación pendiente:** reabrir Revit, seleccionar
"familias exportadas" → deben aparecer las 38 familias.

Hallazgos colaterales verificados: `<WrapPanel IsItemsHost="False"/>` en los templates es
inofensivo (test WPF: el framework fuerza `IsItemsHost=True`); el hueco del Navigator queda
pendiente; los .rfa en la RAÍZ de la fuente siguen sin listarse (ítem UX del fork).

## Diagnóstico previo (sesión 2026-07-18, 3ª)

Estado tras la sesión de diagnóstico del 2026-07-18 (3ª sesión):

- **Descartado:** DLLs desplegadas (92/92, hashes idénticos al publish, ninguna dependencia
  del `deps.json` ausente); versión del build (Scotec 2026.3.2 = RevitYear 2026); culture
  (harness de consola: `RevitFamilyInfo.Initialize()` OK bajo es-VE, es-CO e invariant);
  formato de archivos (pipeline completo `DirectoryFileCache` + `RevitFamilyInfo` replicado
  fuera de Revit sobre la librería real: **191/191 familias OK**).
- **Causa raíz del silencio:** los getters de binding WPF (`FolderViewModel.Families`,
  `.Subfolders`, `FamilySourceViewModel.Folders`) no tenían try/catch; WPF traga cualquier
  excepción de un getter enlazado → panel vacío sin rastro en logs.
- **Sospechoso principal pendiente de confirmar:** `TypeInitializationException` en el static
  ctor de `FamilyViewModel<T>` (URIs `pack://application:,,,/Bim.FamilyManager.Ui;component/...`
  pueden fallar dentro del AssemblyLoadContext aislado de Scotec.Revit.Isolation).
- **Fix desplegado (2026-07-18):** try/catch + `ILogger` en los 3 getters, por ítem en
  `TryCreateViewModel`, y por familia en `DirectorySource.GetFamiliesFromCache` (este último
  es candidato a PR upstream). Con esto la próxima repro deja la excepción real en
  `%AppData%\Autodesk\Revit\Addins\2026\Bim.FamilyManager\Log\Scotec.FamilyManager.log`.
- **Siguiente paso (usuario):** abrir Revit 2026, abrir el panel, seleccionar una carpeta de la
  fuente y luego leer ese log. La excepción registrada da la causa raíz definitiva.
- Datos útiles descubiertos: la fuente por defecto `X:\Familysources` está activa en settings
  pero la unidad no existe (inofensiva: `DirectoryFileCache` la trata como vacía); los .rfa
  sueltos en la RAÍZ de una fuente nunca se listan (el árbol solo muestra subcarpetas) — ítem
  de UX para el fork; el settings se guarda en `%AppData%\Bim.FamilyManager\2025\` aunque sea
  Revit 2026 (revisar de dónde sale el "2025").

## Bloqueos

- Ninguno bloqueante. El push al fork y la aprobación del re-scope de Fase 1 requieren
  acción del usuario (ver Próximos pasos arriba).

## Historial de sesiones

### 2026-07-18 (2ª sesión) — Fase 0: setup, build, licencias, deploy

- **Repo:** `git init` en el workspace + fetch de upstream; rama `develop` (commit `e089d29`,
  2026-05-20) con tag `upstream-base`. Los archivos propios (CLAUDE.md, PROGRESS.md, docs/)
  conviven sin conflicto con el árbol upstream. Falta fork en GitHub (no hay `gh` CLI).
- **Toolchain:** el upstream usa solución `.slnx` + `LangVersion 14.0` → requiere SDK .NET 10.
  Instalado **SDK 10.0.302** vía winget (los SDK 8.0.404/9.0.101 existentes no compilan).
- **Build:** `dotnet build Source/Bim.FamilyManager.slnx -c Release -p:RevitYear=2026 -m:1`
  → limpio (0 errores, 3 warnings esperados: NU1902 OpenMcdf, CS0219 en código generado
  Scotec). **`-m:1` obligatorio**: carrera del markup-compile WPF en builds multiproceso
  (CS2001/BG1002 intermitentes). Multi-target por `-p:RevitYear` (2025 por defecto).
- **Deploy:** `dotnet publish` del proyecto principal genera `Publish/` con el layout que
  espera el `.addin` (`Bim.FamilyManager.addin` + subcarpeta con 92 archivos). Copiado a
  `%AppData%\Autodesk\Revit\Addins\2026\`. Comandos exactos documentados en CLAUDE.md.
- **Licencias:** los 11 paquetes `Scotec.*` son **MIT** (verificado en los `.nuspec` de
  `packages/`). **Veredicto: GO sin reservas** — registrado en ARCHITECTURE.md § Licencias.
- **Metadatos upstream mapeados** (ARCHITECTURE.md § Formato de metadatos): dos schemas de
  Extensible Storage (`FamilyMetadata_V1` = JSON en bytes; `PreviewImages_V1` = PNG Base64
  por tipo) + sidecars `.yaml` camelCase (descriptores con `localizedNames`, `imagePath`,
  `version`). Ambos solo existen si la familia pasó por la herramienta → la Fase 1 debe
  hacer fallback a la extracción nativa y la Fase 2 tratarlos como opcionales.
- **Criterios de aceptación Fase 0:** licencias ✔, CLAUDE.md ✔, build ✔ — falta solo
  carga + colocación verificadas en Revit (smoke test manual pendiente).

### 2026-07-18 (1ª sesión) — Inicialización del workspace (planificación, sin código)

- Análisis completo del código upstream (ver `docs/ARCHITECTURE.md`): colocación vía DoDragDrop + PostRequestForElementTypePlacement confirmada; carga robusta con IFamilyLoadOptions; punto de extensión = delegate `LoadFileStream` de `RevitFamilyInfo`; caché en memoria sin persistencia; previews solo vía sistema propio (EStorage/yaml).
- Deuda técnica upstream catalogada (6 ítems, ver ARCHITECTURE.md § Deuda técnica).
- Plan de 7 fases definido en `docs/PLAN.md` (0-6, ~12-16 sesiones).
- Decisiones de diseño D1-D5 registradas.
- Riesgo principal identificado: licencias de los paquetes NuGet `Scotec.*` — se resuelve en Fase 0 antes de escribir código.
