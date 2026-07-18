# ARCHITECTURE.md — Análisis del upstream y decisiones de diseño

Basado en la revisión del código de `scotec-Software-Solutions-AB/Bim.FamilyManager`, rama `develop` (jul 2026).

## Cómo funciona el upstream

### Cadena de abstracción
```
IFamilyManager  → orquestador: fuentes, búsqueda, carga en documento, previews
IFamilySource   → una fuente de familias (Directory, AzureStorage, …)
IFolder         → nodo del árbol (lazy: delegates para subcarpetas y familias)
IRevitFamily    → una familia (nombre + RevitFamilyInfo + acción de guardado)
IRevitFamilySymbol → un tipo dentro de la familia
```
Los ViewModels de `Abstractions/ViewModels` conectan esto con la UI WPF (FamilyExplorer).

### Puntos clave verificados en código

**Colocación (la parte difícil, ya resuelta):** `Ui/Views/FamilyView.xaml.cs` inicia `UIApplication.DoDragDrop(familyViewModel, dropHandler)`; `Ui/FamilyDropHandler.cs` recibe el drop y llama `uiDocument.PostRequestForElementTypePlacement(symbol)` → Revit entra en modo de colocación nativo. No usar otro mecanismo sin una razón fuerte.

**Carga:** `Base/Logic/FamilyManager.cs` — `TryLoadFamilyIntoActiveDocument` / `TryLoadFamily(document)` escriben el stream a un archivo temporal y llaman `document.LoadFamily(tempFilePath, overwriteOptions, out family)`. Maneja explícitamente: familia ya cargada (LoadFamily falla y no recarga) y símbolos faltantes (carga por símbolo con `LoadFamilySymbol` + `OverwriteFamilyOption : IFamilyLoadOptions`). 

**Abstracción de contenido:** `RevitFamilyInfo` (del paquete NuGet `Scotec.Revit`, namespace `Scotec.Revit.RevitFamily`) encapsula metadatos + un **delegate `Stream LoadFileStream()`**. `DirectorySource.CreateFamilyInfo` devuelve un `MemoryStream` del archivo local. **Consecuencia de diseño: cualquier fuente nueva (índice, HTTP) solo necesita proveer ese delegate.** Es el punto de extensión central del fork.

**Metadatos propios del upstream:** doble sistema — (a) Extensible Storage "BIM.FamilyManager" dentro del .rfa (`Base/Logic/EStorage/*`: `EStorageSchema`, `FamilyMetadataEStorage`, `PreviewImageEStorage`), (b) sidecars `.yaml` por familia (`Descriptors/DescriptorYamlSerializer`). Las previews las escribe `ViewImageWriter` dentro del compound file con OpenMcdf. **Implicación: solo las familias procesadas por la herramienta tienen preview/metadata en este sistema.** Nuestro enfoque (leer streams nativos) es complementario, no reemplazo: fallback EStorage → nativo → ícono.

**Enumeración local:** `Source.Directory/Logic/DirectoryFileCache` — caché en memoria por sesión: `InitializeAsync` hace un escaneo completo (`GetDirectories` + `GetFiles("*.rfa")` recursivo) y llena `_folderFileMap` + `_allFiles`. `DirectorySource.GetFamiliesFromCache` filtra backups con regex, empareja .rfa ↔ .yaml, y crea/reusa `IRevitFamily` vía `FamilyManager.TryGetRevitFamily`/`RegisterRevitFamily`. Guardado con backup previo (`FileBackupHelper`) y notificación por InfoCenter balloon.

## Deuda técnica del upstream (detectada en revisión)

| # | Problema | Ubicación | Fix propuesto |
|---|----------|-----------|---------------|
| 1 | `GetImmediateSubfolders` va a disco (`Directory.GetDirectories`) en cada llamada aunque existe el caché | `DirectoryFileCache` | Derivar subcarpetas de `_folderFileMap` |
| 2 | `GetDescriptionFiles` escanea disco en vivo en cada enumeración | `DirectoryFileCache` | Cachear .yaml en `InitializeAsync` |
| 3 | `catch { }` vacíos tragan errores de IO | `DirectoryFileCache` (varios) | Loggear con `ILogger` |
| 4 | `PreviewStream` estático compartido, `Position = 0` sin sincronización | `DirectorySource.Preview` | Copia por llamada o lock |
| 5 | `BackupRegex` solo `\.\d{4}\.rfa$` | `DirectorySource` | Cubrir patrón real de backups de Revit |
| 6 | `CreateFamilyInfo` carga el .rfa completo a `MemoryStream` | `DirectorySource` | OK para carga puntual; **no** usar esta ruta para indexación masiva (leer streams OLE directo del archivo) |

Los #1-5 son candidatos a PR upstream (Fase 4).

## Decisiones de diseño del fork

### D1 — Librerías puras sin Revit API
`Bim.FamilyManager.Rfa` (lectura OLE) y `Bim.FamilyManager.Index` (SQLite) no referencian RevitAPI.dll. Motivo: testeables con xUnit sin Revit, reutilizables (p. ej. el mismo código puede alimentar procesos server-side de libraryrevit.com), y la indexación corre en background threads donde la Revit API está prohibida de todos modos.

### D2 — Lectura de streams OLE del .rfa (sin abrir Revit)
Un .rfa es un OLE Compound File. Streams de interés:
- `RevitPreview4.0` → miniatura PNG (buscar magic bytes `89 50 4E 47` tras el header propietario).
- `BasicFileInfo` → versión de Revit de guardado (UTF-16; parsear campos "Format"/"Build"). Es lo que Revit usa para el aviso de upgrade.
- `PartAtom` → XML con categoría, tipos y parámetros de la familia (equivalente a `Document.ExtractPartAtomFromFamilyFile` pero sin Revit).
OpenMcdf ya es dependencia del upstream — misma librería, cero dependencias nuevas.
Validar cada lector contra fixtures reales de la librería (distintas versiones de Revit) — los formatos internos tienen variaciones entre versiones; el parser debe ser tolerante y degradar con gracia (campo = null, nunca excepción no controlada).

### D3 — Índice como capa, no como reemplazo destructivo
`IndexedDirectorySource` implementa `FamilySource<TOptions>` consultando SQLite. `DirectorySource` original queda intacta (opción "sin índice" en settings) al menos hasta que el índice esté probado con la librería completa. Minimiza el diff contra upstream.

### D4 — El índice es descartable
`index.db` es un caché derivado: borrar el archivo y reindexar siempre debe reconstruir el estado completo desde el disco. Tags y favoritos son la excepción (datos de usuario) → respaldarlos en un export JSON automático junto al .db.

### D5 — Fuente remota = mismo contrato que las locales
`Source.LibraryRevit` no introduce conceptos nuevos en la UI: es un `IFamilySource` más cuyo `LoadFileStream` descarga por HTTP con verificación SHA256 y caché local. Lo premium/free lo decide el servidor; el cliente solo refleja estado.

## Riesgos abiertos

| Riesgo | Estado | Mitigación |
|--------|--------|------------|
| Licencia de paquetes NuGet `Scotec.*` | **Resuelto (GO)** — ver § Licencias Scotec | — |
| Formatos internos de streams OLE varían por versión de Revit | Conocido | Fixtures multi-versión + parsers tolerantes (D2) |
| Upstream joven puede introducir cambios incompatibles | Aceptado | Tag `upstream-base`, PRs frecuentes, sync periódico |
| Rendimiento WPF con librerías enormes | Por medir en Fase 2 | Virtualización de listas + paginación de resultados |
| Backport Revit 2024 (.NET 4.8) | Diferido a Fase 6 | Decidir con datos de demanda |

## Licencias Scotec (verificado en Fase 0, 2026-07-18)

**Veredicto: GO sin reservas.** Los 11 paquetes `Scotec.*` que restaura la solución declaran
licencia **MIT** como expresión SPDX en su `.nuspec` (verificado en `packages/` tras el restore,
versiones 2026.3.2 para los `Scotec.Revit.*`): `Scotec.Revit`, `Scotec.Revit.Ui`,
`Scotec.Revit.Wpf`, `Scotec.Revit.Isolation`, `Scotec.Queues`, `Scotec.Wpf`,
`Scotec.Wpf.Controls`, `Scotec.Events.WeakEvents`, `Scotec.Identity.AzureActiveDirectory`,
`Scotec.Extensions.Linq`, `Scotec.Extensions.Utilities`. No hace falta reimplementar
`RevitFamilyInfo` ni ninguna otra pieza para comercializar; basta mantener la atribución MIT.

## Formato de metadatos del upstream (mapeado en Fase 0)

El upstream persiste metadatos propios en dos lugares: **Extensible Storage** dentro del
documento de Revit (elementos Family) y **sidecars `.yaml`** junto a los `.rfa`.

### Extensible Storage (`Source/Bim.FamilyManager.Base/Logic/EStorage/`)

Base común: `EStorageSchema` (crea el schema con `AccessLevel.Public` lectura/escritura,
`VendorId = "BIM.FamilyManager"`, campos vía reflexión sobre `Entity.Get/Set`). Dos schemas:

- **`Bim_FamilyManager_FamilyMetadata_V1`** (`FamilyMetadataEStorage`), GUID
  `7DAED877-211A-41B8-BEF4-2CEE567D0C01`. Un campo: `FamilyMetadata: IList<byte>` — JSON UTF-8
  de `FamilyMetadata { Description?, Version, LastModified, ModifiedBy }`.
- **`Bim_FamilyManager_PreviewImages_V1`** (`PreviewImageEStorage`), GUID
  `1F9E97BA-765D-43B8-9100-6FDE3FE3114A`. Campos: `FamilyPreviewImageName: string` y
  `TypePreviewImages: IDictionary<string,string>` — nombre de tipo → imagen PNG en Base64.

Implicación para el fork: estos schemas solo existen en familias que pasaron por la
herramienta. La cadena de fallback de miniaturas (Fase 1) debe consultar primero
`PreviewImageEStorage`/EStorage del upstream y caer a la extracción nativa `RevitPreview4.0`.

### Sidecars YAML (`DescriptorYamlSerializer`, `Abstractions/Descriptors/`)

Serialización con YamlDotNet, convención **camelCase**, UTF-8. Contratos:

- `IItemDescriptor`: `localizedNames: [{language, name}]` (p. ej. `en-US`), `imagePath`.
- `IFileDescriptor : IItemDescriptor`: agrega `version: string`.
- `IFolderDescriptor` / `IFamilySourceDescriptor`: variantes para carpetas y fuentes.

Los `.yaml` se descubren con escaneo recursivo en vivo (`GetDescriptionFiles` — ver Deuda
técnica ítem 2). El índice de Fase 2 debe tratarlos como opcionales: una librería que nunca
pasó por la herramienta no los tiene.

## Registro de decisiones tomadas en desarrollo

- 2026-07-18 — **`IndexedDirectorySource` CONVIVE con `DirectorySource`** (no la reemplaza):
  tipo de fuente nuevo ("Indexed Directory") seleccionable al agregar fuentes. Razones:
  (a) el plan original pedía mantener `DirectorySource` intacta como opción de
  compatibilidad; (b) cero riesgo de regresión para fuentes pequeñas ya configuradas;
  (c) la indexada es opt-in para librerías grandes, que es su caso de uso. Diseño: carpetas
  y familias se sirven del índice (apertura instantánea); un scan incremental corre en
  background en la primera enumeración de la sesión (re-scan ligero al abrir) y en cada
  Reload manual — **el botón refresh del panel ES el comando de reindexado** del lado del
  panel; además el settings de la fuente indexada tiene botón "Reindex now" con resumen
  (útil al agregar una librería grande). Cuando un scan detecta cambios, la fuente se
  recarga sola. Reutiliza `FamilyInfoCache` (mismo flujo hit/miss que DirectorySource).
- 2026-07-18 — Micro-PR candidato a upstream: `DefaultFamilySources.json` trae de fábrica
  una fuente fantasma `X:\Familysources` con `IsEditable: false` — imposible de quitar desde
  la UI y apunta a una unidad que normalmente no existe. En el fork quedó vacío
  (`"Sources": []`); en la máquina del usuario se limpió también el settings.json persistido.
- 2026-07-18 — Métricas de estrés del índice (harness fuera de Revit, set sintético de
  25.000 .rfa reales via hardlinks en 500 carpetas): indexación inicial 25.1 s (997
  familias/s; caché de SO caliente — en librerías reales el cuello será I/O de disco), DB
  22.5 MB, re-scan incremental sin cambios **1.5 s** (objetivo < 10 s ✔), búsqueda FTS
  mediana 0.2-42 ms / máx 56 ms (objetivo < 100 ms ✔). Funcional contra carpeta real
  "prueba" (6 .rfa): 169 ms inicial, 4 ms re-scan, categorías y OmniClass correctos —
  incluidas categorías en español de familias exportadas con Revit ES, confirmando la
  necesidad del `category_key` multiidioma.

(Agregar aquí con fecha cada decisión relevante que se tome durante las fases.)

- 2026-07-18 — Se adopta Bim.FamilyManager (develop) como base del fork. Análisis inicial completado.
- 2026-07-18 — Bug "panel vacío" resuelto: la vista FamilyExplorer no enlazaba las familias
  directas de la carpeta seleccionada (solo `Subfolders` + búsqueda). Fix `DirectFamilies` +
  logging anti-silencio en getters de binding WPF (rama `fix/empty-family-panel`, 2 commits,
  candidatos a PR upstream). El hueco hermano en FamilyNavigator (subcarpetas O familias,
  nunca ambas) queda pendiente.
- 2026-07-18 — **Re-scope de Fase 1** (verificado con harness + código fuente de scotec-revit):
  `Scotec.Revit.RevitFamily.RevitFamilyInfo` ya hace la extracción nativa que la fase planeaba
  construir — `Initialize()` lee con OpenMcdf los streams `RevitPreview4.0` (PNG vía
  `PngExtractor`) y `PartAtom` (título, producto, versión, updated, tipos); `RevitBasicFileInfo`
  cubre `BasicFileInfo`. Sin tipos de Revit API en ese namespace (usable y testeable fuera de
  Revit — demostrado). Lo que NO da y sigue siendo nuestro: **persistencia** (todo es
  memoria por sesión), **categoría** de familia (el PartAtom la tiene, `LoadPartAtom` no la
  lee) y **lectura eficiente** (el upstream copia el .rfa completo a memoria: 145 MB leídos
  para 361 KB de previews en la librería de prueba). Medición (191 familias, caché SO
  caliente): 0.9-1.7 ms/familia. Decisión: no construir `RfaThumbnailReader` propio; Fase 1 =
  `ThumbnailCache` persistente + `PartAtomCategoryReader` + apertura directa sobre
  `FileStream`. **D1 se refina:** el requisito real de las librerías nuevas es "testeable sin
  Revit instalado", y referenciar `Scotec.Revit` lo cumple; deja de exigirse "sin referencia a
  RevitAPI.dll" en sentido estricto.
- 2026-07-18 — PRs upstream menores detectados en scotec-revit: exponer `Category` en
  `RevitFamilyInfo`; `DateTime.Parse` del elemento `updated` del PartAtom sin
  `CultureInfo.InvariantCulture` (funciona con ISO-8601, pero es frágil).
- 2026-07-18 — **Fase 1, tarea 2 re-resuelta** (lectura sin copiar el archivo entero en
  `DirectorySource`): NO se cambia el `LoadFileStream` del upstream. Causa raíz descubierta en
  scotec-revit: `RevitFamilyInfo.Initialize()` obtiene el stream del loader
  (`RootStorage.Open(GetFamilyStream(), LeaveOpen)`) y **nunca lo dispone** — con un
  `FileStream` directo cada extracción dejaría un handle abierto hasta el GC, pudiendo
  bloquear el guardado de familias desde Revit ("file in use"). Además `Family` invoca el
  loader en cada acceso sin que los consumidores dispongan el stream. Con el
  `MemoryStream` actual el "leak" es solo memoria gestionada (inofensivo). El costo de la
  copia completa queda amortizado: ocurre una sola vez por familia (sesión 1, y la misma
  lectura alimenta el `FamilyInfoCache`); desde la sesión 2 no se lee nada. La lectura por
  sectores vive donde controlamos el ciclo de vida: `PartAtomCategoryReader` y el scanner de
  la Fase 2. **PR upstream candidato (scotec-revit):** `Initialize()` debe disponer el
  stream que obtiene del loader.
- 2026-07-18 — **Categorías multiidioma en el índice (Fase 2):** el `PartAtom` NO contiene un
  identificador estable de la categoría de Revit — solo el término localizado al idioma de
  autoría (scheme `adsk:revit:grouping`) y, únicamente en familias de modelo, el número
  OmniClass (scheme `std:oc1`, taxonomía distinta y editable por el usuario; las familias de
  anotación no lo llevan — verificado en fixtures). Decisión: el schema captura los tres
  datos crudos — `category` (texto localizado, para mostrar y filtrar tal cual), `omniclass`
  (nullable) y `category_key` (nullable, **reservado**) — y la unificación
  "Furniture"↔"Mobiliario" se implementa en la Fase 3 con un mapa de localización de las
  categorías estándar de Revit que rellena `category_key`, sin migración de schema.
- 2026-07-18 — Fase 2, ajuste de lectores: `PartAtomCategoryReader` se generalizó a
  `PartAtomReader` (título, product-version, updated, categoría, OmniClass y nombres de
  tipos en una pasada). `BasicFileInfoReader` se pospone: el `product-version` del PartAtom
  cubre la necesidad de la Fase 3 (aviso de versión) y `RevitBasicFileInfo` de Scotec queda
  disponible si hiciera falta más (worksharing, etc.). Dato verificado: el product-version
  del PartAtom es autoritativo — un fixture llamado "v2025" está realmente guardado en 2026.
- 2026-07-18 — Integración del caché en el panel: `ThumbnailCache` se generalizó a
  `FamilyInfoCache` (PNG + JSON con Product/ProductVersion/Updated/HasThumbnail por entrada,
  el JSON se escribe último como marcador de entrada completa) porque servir solo la
  miniatura dejaría el badge de versión vacío en los hits y obligaría a leer el archivo
  igualmente. Asiento del caché: `RevitFamily.ApplyCachedInfo(...)` (Base) marca la familia
  como inicializada con datos del caché — la cola de inicialización la salta y el .rfa no se
  lee; `Family`/`FamilySymbols` siguen leyendo bajo demanda (interacción). En miss, el
  handler del evento `Initialized` guarda lo extraído; la invalidación por
  mtime/size cubre el caso de familias re-guardadas (`SaveFamily`).
