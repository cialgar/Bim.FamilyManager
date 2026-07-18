# PLAN.md — Plan de trabajo por fases

Estimaciones en "sesiones" = sesiones de trabajo con Claude Code (~1-3 h efectivas).
Regla general: una fase no se cierra sin cumplir sus criterios de aceptación y sin actualizar PROGRESS.md.

---

## Fase 0 — Setup, auditoría y decisión GO/PIVOT (1 sesión)

La fase más importante: valida que construir sobre este fork es viable legal y técnicamente.

### Tareas
1. Fork del repo en GitHub (`scotec-Software-Solutions-AB/Bim.FamilyManager` → cuenta propia), clonar rama `develop` dentro de este workspace.
2. `dotnet build` limpio. Documentar en CLAUDE.md el nombre real del `.sln`, warnings existentes y el output path de las DLLs.
3. Desplegar en Revit 2026: copiar DLLs + `.addin` a `%AppData%\Autodesk\Revit\Addins\2026\`. Smoke test manual: abrir panel, configurar una fuente Directory con una carpeta de prueba (~50 .rfa reales de la librería), navegar, cargar una familia, colocarla con drag & drop.
4. **Verificación de licencias** de `Scotec.Revit`, `Scotec.Revit.Ui`, `Scotec.Queues` (nuget.org → license). Registrar el resultado en ARCHITECTURE.md.
   - Si son MIT/Apache/permisivas → **GO** sin reservas.
   - Si son propietarias/restrictivas → evaluar: (a) seguir usándolas solo para uso interno, (b) reimplementar `RevitFamilyInfo` y las piezas mínimas (es la clase puente clave; su superficie es pequeña: nombre + metadata + delegate de stream). Decidir antes de la Fase 5 (comercialización).
5. Mapear el formato de metadatos propio del upstream: EStorage "BIM.FamilyManager" + sidecars `.yaml`. Documentar el schema en ARCHITECTURE.md (leerlo de `EStorageSchema.cs`, `FamilyMetadataEStorage.cs`, `DescriptorYamlSerializer.cs`).
6. Crear tag `upstream-base` en git para poder diffear contra el upstream siempre.

### Criterios de aceptación
- [x] Build limpio documentado, add-in funcionando en Revit 2026 con carga + colocación verificadas. *(2026-07-18: smoke test completo del usuario — panel, fuente Directory, navegación, carga y colocación drag & drop; requirió el fix `DirectFamilies`, ver PROGRESS.md)*
- [x] Veredicto de licencias escrito en ARCHITECTURE.md con decisión GO/PIVOT. *(GO sin reservas: los 11 paquetes Scotec.* son MIT)*
- [x] CLAUDE.md actualizado con comandos reales de build/deploy. *(incluye SDK .NET 10, -m:1, publish y deploy corregido)*

> **Fase 0 CERRADA (2026-07-18).** Único residuo: push al fork de GitHub (pendiente de URL); el tag `upstream-base` existe en local.

---

## Fase 1 — Caché persistente de miniaturas y lectura eficiente del .rfa (1-2 sesiones)

> **Re-scope 2026-07-18** (decisión en ARCHITECTURE.md § Registro): la extracción nativa que
> esta fase planeaba construir YA EXISTE en `Scotec.Revit.RevitFamily.RevitFamilyInfo` (MIT,
> sin tipos de Revit API): `Initialize()` lee el compound file con OpenMcdf y extrae preview
> (`RevitPreview4.0` → `PngExtractor`), título/producto/versión/updated y tipos (`PartAtom`).
> El panel ya la usa en background (`TaskQueue`, DOP 4) — verificado con el panel funcionando
> y con harness de consola. Medido sobre la librería real (191 familias, 145 MB, caché de SO
> caliente): 0.9-1.7 ms/familia. El problema real es otro: **(a)** el flujo del upstream copia
> el .rfa COMPLETO a memoria para extraer ~2 KB de PNG (145 MB leídos por sesión para 361 KB
> de previews), y **(b)** nada persiste — cada sesión de Revit re-extrae todo. A decenas de
> miles de familias eso son GBs releídos por arranque.

### Tareas
1. Crear proyecto `Bim.FamilyManager.Rfa` + tests xUnit. Puede referenciar `Scotec.Revit`
   (el requisito real de D1 es "testeable sin Revit", y su namespace `RevitFamily` lo cumple —
   verificado). Contenido:
   - `ThumbnailCache`: PNGs en `%AppData%\FamilyManager\thumbnails\`, clave =
     hash(ruta + mtime + tamaño), invalidación automática al cambiar el archivo, tolerante a
     caché corrupto/borrado (se regenera).
   - `PartAtomCategoryReader`: lector mínimo de la **categoría** de familia del XML `PartAtom`
     (lo único que `RevitFamilyInfo` no expone; necesario para filtros de Fases 2-3).
     Candidato a PR upstream: exponer `Category` en `RevitFamilyInfo`.
2. ~~Lectura eficiente vía `FileStream` en `DirectorySource.CreateFamilyInfo`~~ **Re-resuelta
   (ver ARCHITECTURE § Registro, 2026-07-18):** `RevitFamilyInfo.Initialize()` de Scotec no
   dispone el stream del loader — un `FileStream` ahí dejaría handles abiertos hasta el GC y
   podría bloquear guardados desde Revit. El loader se mantiene; el costo de la copia
   completa es una sola vez por familia (y alimenta el caché); la lectura por sectores vive
   en `PartAtomCategoryReader` y en el scanner de Fase 2. PR candidato a scotec-revit.
3. Integración en el panel (hecha): el caché se generalizó a `FamilyInfoCache` (PNG +
   metadata JSON — solo miniatura dejaría el badge de versión vacío en los hits).
   `RevitFamily.ApplyCachedInfo` (Base) marca la familia como inicializada desde el caché →
   la cola de fondo la salta y el .rfa no se lee; en miss, el evento `Initialized` guarda lo
   extraído. `Family`/`FamilySymbols` siguen leyendo bajo demanda. La cadena de fallback
   visual del upstream (EStorage → nativa → ícono) se conserva.
4. Tests con .rfa reales (5-10 fixtures multi-versión en `tests/fixtures/`, incluyendo uno
   corrupto y uno sin preview) para `ThumbnailCache` y `PartAtomCategoryReader`.

### Criterios de aceptación
- [x] Tests xUnit verdes (hit/miss/invalidación/corrupto del caché; categoría correcta de
      fixtures contrastada con lo que muestra Revit). *(20/20 verdes; categorías de los 4
      fixtures validadas por el usuario en Revit, 2026-07-18)*
- [x] Segunda apertura de Revit sobre la misma carpeta: previews servidas desde el caché con
      **0 bytes leídos** de los .rfa no modificados (verificable por log). *(Verificado por
      el usuario: sesión B con 0 extracciones y 42 familias servidas desde caché, miniaturas
      y badges instantáneos)*
- [x] Modificar un .rfa (mtime/size) → su miniatura se re-extrae y la entrada vieja se invalida.
      *(Cubierto por tests de invalidación de `FamilyInfoCache`; el mecanismo mtime/size es el
      mismo en runtime)*
- [x] Scroll fluido con ~200 familias en una carpeta (hoy ya se cumple; no regresionar).
      *(Sin regresión reportada con las carpetas reales tras el deploy)*

> **Fase 1 CERRADA (2026-07-18).**

---

## Fase 2 — Índice SQLite persistente (2-3 sesiones)

Reemplaza el escaneo por sesión de `DirectoryFileCache` por un índice persistente con refresh incremental. Es el corazón del fork.

### Tareas
1. ~~Extender `Bim.FamilyManager.Rfa` con dos lectores más~~ **Hecha (ajustada):**
   `PartAtomCategoryReader` → `PartAtomReader` (título, product-version, updated, categoría
   localizada, OmniClass y nombres de tipos en una pasada). `BasicFileInfoReader` pospuesto:
   el product-version del PartAtom cubre el aviso de versión de Fase 3 (ver ARCHITECTURE).
2. **Hecha:** proyecto `Bim.FamilyManager.Index` (librería pura: Microsoft.Data.Sqlite + Rfa)
   + tests. Schema real (ajustado por el hallazgo de categorías multiidioma — ver
   ARCHITECTURE § Registro):
     ```sql
     CREATE TABLE sources   (id INTEGER PK, root_path TEXT UNIQUE, last_scan_utc TEXT);
     CREATE TABLE families  (id INTEGER PK, source_id INT→sources, path TEXT UNIQUE, name TEXT,
                             folder TEXT, size INT, mtime_utc_ticks INT, product_version TEXT,
                             category TEXT,      -- término localizado tal como está en el .rfa
                             category_key TEXT,  -- reservado: clave normalizada (mapa Fase 3)
                             omniclass TEXT,     -- número OmniClass si existe (solo modelo)
                             updated_utc TEXT, indexed_at_utc TEXT);
     CREATE TABLE symbols   (id INTEGER PK, family_id INT→families, name TEXT);
     CREATE TABLE tags      (id INTEGER PK, name TEXT UNIQUE);
     CREATE TABLE family_tags (family_id, tag_id, PK(family_id, tag_id));
     CREATE TABLE favorites (family_id INT PRIMARY KEY);
     CREATE VIRTUAL TABLE families_fts USING fts5(name, folder, category, symbols);
     -- FTS con contenido propio y rowid = families.id (contentless exigiría SQLite 3.43+ para deletes)
     ```
   - `IndexScanner` (hecho): escaneo incremental — enumera disco tolerando carpetas
     inaccesibles, compara `(path, mtime, size)`, re-extrae solo nuevos/modificados vía
     `PartAtomReader` (lectura por sectores), elimina desaparecidos, todo en una transacción;
     `IProgress<ScanProgress>`; archivos ilegibles se indexan con datos básicos (findables
     por nombre) y se reportan en `FailedExtractions`.
   - `IndexQuery` (hecho): FTS5 con prefijo por token + filtros combinables (categoría,
     versión, carpeta con subcarpetas, favoritos, tag) + `GetCategories()`.
3. **Hecha:** `IndexedDirectorySource` — tipo de fuente nuevo que CONVIVE con `DirectorySource`
   (decisión y diseño en ARCHITECTURE § Registro): carpetas/familias desde el índice, scan
   incremental en background al abrir y en cada Reload (el refresh del panel = reindexar).
4. **Hecha:** "Reindex now" en el settings de la fuente indexada (con resumen) + re-scan
   automático ligero al abrir (primera enumeración de la sesión).
5. **Hecha:** estrés funcional (carpeta real `prueba`, 6 .rfa: 169 ms / 4 ms) y de escala
   (sintético 25.000: inicial 25.1 s, re-scan 1.5 s, FTS máx 56 ms) — métricas en PROGRESS.md.

### Criterios de aceptación
- [x] Tests verdes de PartAtom con fixtures reales (categoría y tipos verificados contra Revit
      por el usuario; `BasicFileInfoReader` pospuesto — ver ajuste de tareas). *(35/35 tests)*
- [x] Índice completo de la librería real construido; métricas registradas en PROGRESS.md.
      *(Smoke test del usuario: 191 familias indexadas en 1.7 s, 0 fallos)*
- [x] Segundo arranque de Revit: panel poblado desde el índice sin escaneo completo.
      *(Re-scan incremental de 36 ms verificado por el usuario; navegación y drag & drop OK)*
- [x] Buscar por nombre de tipo (no solo de archivo) devuelve resultados correctos.
      *(Test FTS "96" → familia por nombre de tipo; búsqueda global en UI llega en Fase 3)*

> **Fase 2 CERRADA (2026-07-18).**

---

## Fase 3 — Búsqueda y UX (2-3 sesiones)

1. Caja de búsqueda global sobre FTS5 (hoy `FilterFamilies` solo filtra el árbol cargado por
   nombre de archivo) con resultados agregados de todas las fuentes indexadas, servida por
   `IndexQuery.Search`.
2. Filtros combinables: categoría, versión de Revit, tags, favoritos. Las categorías filtran
   por `category_key` cuando existe (unifica "Furniture"↔"Mobiliario") con fallback al texto
   localizado.
3. Mapa de localización de categorías (`CategoryKeyMap` en Index): tabla EN/ES de las
   categorías estándar de familias de Revit → clave estable; el scanner rellena
   `category_key` al extraer y un pase post-scan lo completa para filas ya indexadas cuando
   el mapa crece. Términos fuera del mapa → key null (se filtra por texto localizado).
4. Tags y favoritos: API en Index (persisten en el índice, no tocan los .rfa) + menú
   contextual sobre la tarjeta de familia.
5. Vista galería (grid de miniaturas) además del árbol, aprovechando Ui.Standard/Ui.Modern.
6. Aviso de versión al cargar: comparar el `product-version` del PartAtom (autoritativo —
   verificado que el nombre de archivo puede mentir) contra la versión del documento activo:
   mayor → bloquear con mensaje claro; menor → advertir que Revit actualizará el archivo.
7. **Inserción con clic** (alternativa al drag & drop): doble clic en la tarjeta coloca el
   tipo por defecto de la familia; clic sobre un tipo específico (familias multi-tipo)
   coloca ese tipo. Ambos caminos: cargar la familia si hace falta y disparar
   `PostRequestForElementTypePlacement` vía `ExternalEvent`/`IExternalEventHandler` (estudiar
   primero cómo lo hace `FamilyDropHandler`, según regla del proyecto). Validación previa:
   si la vista activa no admite la categoría de la familia → aviso claro al usuario; nunca
   fallo silencioso.

### Criterios de aceptación
- [ ] Buscar "silla" muestra resultados de toda la librería (todas las fuentes indexadas)
      en < 1 s con miniaturas.
- [ ] Filtrar por categoría unifica variantes de idioma: "Furniture" y "Mobiliario" caen en
      el mismo filtro (via `category_key`); categorías fuera del mapa siguen filtrables por
      su texto.
- [ ] Tags/favoritos sobreviven reinicio de Revit.
- [ ] Intento de cargar una familia 2026 en un documento 2025 se bloquea con aviso, no con
      el error críptico de Revit; familia antigua avisa del upgrade.
- [ ] Doble clic en una tarjeta coloca el tipo por defecto en la vista activa; clic en un
      tipo específico coloca ese tipo.
- [ ] Con una vista activa que no admite la categoría (p. ej. familia 3D en una leyenda),
      la inserción muestra un aviso claro y no hace nada más.

---

## Fase 4 — Correcciones upstream y PRs (1 sesión, paralelizable)

Deuda técnica detectada en la revisión de código (detalle en ARCHITECTURE.md § Deuda técnica):

1. `DirectoryFileCache.GetImmediateSubfolders` va a disco en cada llamada pese a ser "cache" → servir desde `_folderFileMap`.
2. `GetDescriptionFiles` hace escaneo en vivo (`Directory.GetFiles` recursivo) en cada enumeración → cachear junto a los .rfa.
3. `catch { }` vacíos silencian errores de IO → loggear con el `ILogger` ya inyectado.
4. `PreviewStream` estático compartido con `Position = 0` no es thread-safe → devolver copia o sincronizar.
5. `BackupRegex` solo reconoce backups de 4 dígitos (`.0001.rfa`) → cubrir el patrón real de Revit.
6. Preparar como PRs pequeños e independientes al upstream (buena relación con scotec + visibilidad; mantener el fork cerca de upstream reduce el costo de sincronizar).

### Criterios de aceptación
- [ ] Fixes con tests donde sea posible; al menos 2 PRs abiertos upstream.

---

## Fase 5 — Fuente remota LibraryRevit (3-4 sesiones)

Retoma el diseño previo del plugin thin-client (API REST api.libraryrevit.com), ahora como una fuente más del panel. `Source.AzureStorage` es la plantilla estructural; `RevitFamilyInfo` ya acepta un delegate de stream, así que la descarga HTTP encaja de forma natural.

### Tareas
1. Definir contrato de API v1 (documento en `docs/API-LIBRARYREVIT.md`): `GET /catalog` (árbol de categorías), `GET /families?query=&page=` (metadatos + URL de miniatura), `GET /families/{id}/download` (URL firmada, requiere API key), auth por API key de cuenta libraryrevit.
2. Implementar el backend mínimo en libraryrevit.com (fuera de este repo — proyecto WordPress/WPDM aparte; aquí solo se consume).
3. `Bim.FamilyManager.Source.LibraryRevit`: implementación de `FamilySource<LibraryRevitSourceOptions>` (API key en settings), carpetas = categorías del catálogo, familias con thumbnail remoto cacheado.
4. Descarga: stream HTTP → verificación SHA256 (el API devuelve el hash) → caché local en `%AppData%\FamilyManager\downloads\` → indexar la copia local para que quede disponible offline.
5. Free vs premium: el API decide qué puede descargar la key; el add-in solo muestra el estado (candado/CTA de upgrade con link a la web).
6. Telemetría mínima y respetuosa: contador de descargas por familia (lado servidor, ya implícito en el endpoint).

### Criterios de aceptación
- [ ] Con API key válida: navegar catálogo, buscar, descargar y colocar una familia premium de punta a punta.
- [ ] Sin conexión: las familias ya descargadas siguen disponibles desde el caché/índice.
- [ ] Key inválida o sin permisos → mensajes claros, nunca crash.

---

## Fase 6 — Distribución (2 sesiones)

1. Branding del fork (nombre propio, íconos, tab del ribbon) manteniendo atribución MIT al upstream.
2. Instalador: partir de `Installation/` del upstream; MSI que despliegue a 2025 y 2026 según lo instalado.
3. Decisión backport Revit 2024 (.NET Framework 4.8 + multi-targeting): evaluar costo real tras medir demanda; registrar decisión en ARCHITECTURE.md. No bloquear el lanzamiento por esto.
4. Página de descarga en libraryrevit.com + post de lanzamiento; el add-in gratuito con fuente local completa es el embudo hacia la fuente remota premium.
5. (Opcional) Autodesk App Store: evaluar requisitos de firma y revisión.

### Criterios de aceptación
- [ ] Instalador probado en máquina limpia con Revit 2026.
- [ ] Página de descarga publicada.

---

## Orden y dependencias

```
Fase 0 ──► Fase 1 ──► Fase 2 ──► Fase 3 ──► Fase 5 ──► Fase 6
              │                     
              └──► Fase 4 (paralelo desde Fase 1)
```

Total estimado: 12-16 sesiones. El valor para uso propio llega al cerrar la Fase 3; las fases 5-6 son la capa de negocio.
