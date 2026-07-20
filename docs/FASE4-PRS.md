# FASE4-PRS.md — PRs a upstream e issues a scotec-revit

> **DECISIÓN (2026-07-19): los PRs y los issues NO se abren por ahora.** El proyecto
> sigue con el fork actual como remote y la decisión de privatizar el repo queda para el
> final. Este documento se conserva íntegro como material listo por si se cambia de
> opinión: las 6 ramas siguen pusheadas al fork y los cuerpos de abajo siguen siendo
> válidos mientras `develop` upstream siga en `e089d29` (si upstream avanza, rebasear las
> ramas antes de abrir). Los 4 fixes nuevos ya están fusionados en `feature/rfa-cache`,
> así que el fork no depende de que upstream los acepte.

Preparado el 2026-07-19. Las 6 ramas están pusheadas al fork (`cialgar/Bim.FamilyManager`),
cada una basada en `develop` upstream (`e089d29`), compiladas sin errores con
`-p:RevitYear=2026`. No hay `gh` CLI, así que los PRs se abren desde la web: para cada PR,
abrir la **URL de compare**, verificar que la base sea `scotec-Software-Solutions-AB/Bim.FamilyManager`
rama **`develop`**, pegar el título y el cuerpo, y crear el PR.

Orden de apertura recomendado = orden de valor para scotec (el del panel vacío primero:
corrige la experiencia por defecto). Los PRs 3-6 son independientes entre sí y de los dos
primeros. El PR 1 **contiene** el commit del PR 2 (dependencia real de código: usa el
logger y el helper que introduce el PR 2); ambos cuerpos lo explican.

---

## PR 1 — Panel vacío con familias directas (PRIORITARIO)

- **Rama:** `fix/empty-family-panel` (commits `2f1010f` + `0183033`)
- **URL:** https://github.com/scotec-Software-Solutions-AB/Bim.FamilyManager/compare/develop...cialgar:Bim.FamilyManager:fix/empty-family-panel?expand=1
- **Título:** `Show the families stored directly in the selected folder`

```markdown
## Bug

The Family Explorer view only renders the subfolders of the folder selected in the
combo box. Families whose .rfa files are direct children of the selected folder have
no visual representation at all: the `FolderView` sections only display the families
of their own subfolder, and the search view requires an active search pattern of at
least 3 characters. A folder without subfolders therefore always appears completely
empty, no matter how many families it contains.

## How to reproduce

1. Add a directory source whose root contains a folder with .rfa files and no
   subfolders (e.g. `C:\Families\Exported` with 38 .rfa files).
2. Open the Family Explorer pane and select that folder in the combo box.
3. The panel shows nothing — no families, no error, nothing in the log.

## Fix

- New `DirectFamilies` property on `FolderViewModel<TLayoutOptions>` that returns only
  the families located directly in the folder (`GetFamiliesAsync(includeSubfolders:
  false)`), lazily and with the same expanded/selected guard as the existing
  `Families` property.
- A new section in `FamilyManagerView.xaml` renders those families above the existing
  subfolder sections, inside the same scroll viewer and with the same `FamilyView`
  item template, so drag & drop keeps working unchanged.

## Notes

- This branch includes the commit from the error-logging PR
  (`fix/log-binding-errors`) because `DirectFamilies` reuses the injected logger and
  the per-item `TryCreateViewModel` guard introduced there. If that PR is merged
  first, this one rebases down to a single commit.
- Verified in Revit 2026 against a real library: the previously empty folder now
  shows all 38 families with previews, and drag & drop into the model works.
- `FamilyNavigator` has a sibling gap (`FamilyManagerViewModel` shows only subfolders
  whenever any exist). This PR deliberately touches only the Explorer; happy to
  follow up on the Navigator if you want the same treatment there.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

---

## PR 2 — Logging de errores tragados por bindings WPF

- **Rama:** `fix/log-binding-errors` (commit `2f1010f`)
- **URL:** https://github.com/scotec-Software-Solutions-AB/Bim.FamilyManager/compare/develop...cialgar:Bim.FamilyManager:fix/log-binding-errors?expand=1
- **Título:** `Log and isolate errors while loading folders and families`

```markdown
## Bug

WPF data binding silently swallows any exception thrown by a bound property getter.
The `Folders`, `Subfolders` and `Families` getters of the view models do their work
(async enumeration, view-model creation) directly inside the getter, so any error
raised there — an IO failure, a corrupt family file, a resource that fails to load —
surfaces as an *empty panel* with no trace in the log. That is exactly how we hit it:
a completely blank Family Explorer with zero diagnostics (see the companion PR that
fixes the underlying display gap).

Additionally, a single faulty entry discarded the whole collection: one unreadable
family file aborted the enumeration of all remaining families in the folder.

## Fix

- Wrap the bodies of `FamilySourceViewModel.Folders`, `FolderViewModel.Subfolders`
  and `FolderViewModel.Families` (Explorer and Navigator) in try/catch and log
  errors through the already-injected `ILogger` instead of losing them.
- Create item view models individually via a `TryCreateViewModel` helper, so a
  single faulty entry is logged and skipped instead of discarding the collection.
- Guard the per-family creation in `DirectorySource.GetFamiliesFromCache` so one
  unreadable family file cannot abort the enumeration of the remaining families.

## Justification

No behavior change on the happy path — the change only affects what happens when
something already went wrong: instead of an empty panel and a support mystery, the
log contains the exception and the remaining items still render.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

---

## PR 3 — DirectoryFileCache que realmente cachea (+ logging + Reload)

- **Rama:** `fix/directory-file-cache` (commit `6104528`)
- **URL:** https://github.com/scotec-Software-Solutions-AB/Bim.FamilyManager/compare/develop...cialgar:Bim.FamilyManager:fix/directory-file-cache?expand=1
- **Título:** `Serve all DirectoryFileCache queries from the cache`

```markdown
## Bug

`DirectoryFileCache` documents that after `InitializeAsync` "all queries are
performed without additional disk access", but two of its three queries still scan
the file system on every call:

- `GetImmediateSubfolders` calls `Directory.GetDirectories` every time it is
  invoked — once per folder node while the tree is being built.
- `GetDescriptionFiles` runs a live `Directory.GetFiles("*.yaml")` scan on every
  family enumeration — recursively when `includeSubfolders` is set.

On large libraries, especially on network shares, these repeated scans dominate the
cost of opening a source. The empty `catch { }` blocks around the scans also swallow
IO errors (e.g. access denied), so problems are invisible to users and support.

Related: `DirectorySource.OnReload` resets the folder view models but keeps the
stale `DirectoryFileCache`, so today a Reload mixes fresh subfolder listings (live
disk calls) with outdated family listings (cached) — files added after the initial
scan appear as folders but never as families.

## Fix

- `InitializeAsync` now also builds a parent→subfolders map and a per-folder map of
  description files, next to the existing family file map. `GetImmediateSubfolders`
  and `GetDescriptionFiles` are served from those dictionaries, exactly like
  `GetFamilyFiles` already is.
- The cache accepts an optional `ILogger` and reports inaccessible directories as
  warnings while keeping the tolerant "treat as empty" behavior. `DirectorySource`
  passes its injected logger.
- `DirectorySource.OnReload` also drops the file cache, so Reload rescans the
  directory tree and actually picks up files added or removed on disk.

## Justification

This aligns the implementation with the class's own documented contract and makes
folder expansion O(1) after the initial scan. The Reload change is required for
correctness once subfolders are served from the cache — and it fixes the existing
stale-family behavior as a side effect.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

---

## PR 4 — PreviewStream compartido no thread-safe

- **Rama:** `fix/preview-stream-thread-safety` (commit `f417981`)
- **URL:** https://github.com/scotec-Software-Solutions-AB/Bim.FamilyManager/compare/develop...cialgar:Bim.FamilyManager:fix/preview-stream-thread-safety?expand=1
- **Título:** `Return a fresh preview stream on each access`

```markdown
## Bug

`DirectorySource`, `AzureStorageSource` and `Folder` expose their preview icon
through a single static `MemoryStream` shared by every instance and every caller.
The `Preview` getter resets `Position` to 0 and hands out the same stream object,
which fails in two ways:

- Concurrent readers race on the shared `Position`: two consumers reading the icon
  at the same time corrupt each other's reads and render a broken image.
- The XML documentation of `LoadResourceAsStream` states that "the caller is
  responsible for disposing the returned stream". Any consumer that honors that
  contract and disposes the preview stream breaks the icon for the entire session:
  every later access throws `ObjectDisposedException`, which WPF bindings swallow
  silently (the icon just disappears).

## How to reproduce

Call `source.Preview.Dispose()` once (as the documented contract invites), then open
any view that binds the preview — the icon never renders again and nothing is logged.

## Fix

Cache the resource as an immutable `byte[]` and return a new read-only
`MemoryStream` over it on each access. Streams can now be read and disposed
independently; the image bytes are still loaded only once per class, so there is no
extra IO — only a small per-access allocation for the stream wrapper.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

---

## PR 5 — Filtro de backups incompleto

- **Rama:** `fix/backup-file-pattern` (commit `8196d34`)
- **URL:** https://github.com/scotec-Software-Solutions-AB/Bim.FamilyManager/compare/develop...cialgar:Bim.FamilyManager:fix/backup-file-pattern?expand=1
- **Título:** `Match all Revit backup files in the backup filter`

```markdown
## Bug

The `BackupRegex` that hides backup files from the family list (`\.\d{4}\.rfa$`,
case-sensitive) misses two kinds of legitimate backup files, which then show up in
the panel as ordinary families (e.g. a family named "Door.0001"):

- **Uppercase extensions.** A backup of `DOOR.RFA` is created as `DOOR.0001.RFA`.
  Windows file enumeration is case-insensitive and finds the file, but the
  case-sensitive regex does not match it. Note that `FileBackupHelper` already
  matches existing backups with `RegexOptions.IgnoreCase` — the two filters
  currently disagree with each other.
- **Sequence numbers with more than four digits.** Backup counters are only ever
  incremented, never reset. After backup 9999 the next backup is written with five
  digits (`Door.10000.rfa`): the `D4` format specifier used by `FileBackupHelper`
  pads to at least four digits but never truncates, and Revit's own save backups
  behave the same way. Those files are not matched either.

## Fix

`\.\d{4,}\.rfa$` with `RegexOptions.IgnoreCase`, applied to the two copies of the
regex (`DirectorySource` and `AzureStorageSource`).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

---

## PR 6 — Fuente por defecto fantasma `X:\Familysources`

- **Rama:** `fix/default-family-source-placeholder` (commit `b2f92b2`)
- **URL:** https://github.com/scotec-Software-Solutions-AB/Bim.FamilyManager/compare/develop...cialgar:Bim.FamilyManager:fix/default-family-source-placeholder?expand=1
- **Título:** `Ship no default family source instead of a placeholder path`

```markdown
## Bug

`DefaultFamilySources.json` ships a "My Family Source" entry pointing to
`X:\Familysources`, which looks like a leftover from an internal environment.
`SettingsManager.ApplyDefaultSettings` merges the file into every user's settings
with `IsEditable: false`, so every fresh installation ends up with a permanent
family source that points to a drive that does not exist on the user's machine and
that the settings dialog allows neither editing nor removing — only deactivating.

## Fix

Ship an empty `Sources` array. The file stays in place and keeps working as a
deployment hook: organizations can add their own entries to preconfigure
company-wide sources, which appears to be the intent of the merge mechanism.

If the entry is intentional (as a visible example for users), an alternative would
be shipping it with a path under `%USERPROFILE%\Documents` instead — happy to adjust
the PR either way.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

---

# Issues a abrir en `scotec-Software-Solutions-AB/scotec-revit`

Afectan al paquete NuGet `Scotec.Revit` (namespace `Scotec.Revit.RevitFamily`,
observado en la versión 2026.3.2). No preparamos PRs porque el código vive en otro
repo y son cambios de API que el maintainer querrá decidir; quedan como issues.
URL: https://github.com/scotec-Software-Solutions-AB/scotec-revit/issues/new

## Issue A — Exponer la categoría de la familia en `RevitFamilyInfo`

- **Título:** `Expose the family category in RevitFamilyInfo`

```markdown
`RevitFamilyInfo` already parses the family's PartAtom (it exposes `Product`,
`ProductVersion` and `Updated` from it), but it does not expose the family
**category** (`category/term` entries of the PartAtom XML), even though the
category is present in the same data it already reads.

Consumers that need the category — e.g. to group or filter families in a library
UI — currently have to re-open and re-parse the .rfa themselves, duplicating work
the class has already done.

Suggestion: expose the raw category term (and, if available, the OmniClass entry)
as string properties, populated during the same parse, `null` when absent.
Annotation families, for instance, carry no OmniClass entry, and the category term
is localized to the language of the Revit build that saved the family — so plain
nullable strings with no further interpretation seem like the right shape.
```

## Issue B — Parseo de `updated` dependiente de la culture

- **Título:** `Parse the PartAtom "updated" timestamp with InvariantCulture`

```markdown
The `updated` element of the PartAtom is written by Revit in a fixed,
culture-independent format, but `RevitFamilyInfo` parses it with the thread's
current culture. In our tests the parse happens to survive common Western
cultures (we verified es-VE and es-CO), but it is a latent correctness issue:
cultures with a non-Gregorian default calendar (e.g. ar-SA, th-TH) reinterpret
the parsed components, so `Updated` depends on the machine's regional settings
rather than on the file's content.

Suggestion: parse with `CultureInfo.InvariantCulture` (and
`DateTimeStyles.RoundtripKind` if the value carries an offset), falling back to
`null` on failure instead of throwing.
```

## Issue C — `Initialize()` no dispone el stream del delegate `LoadFileStream`

- **Título:** `RevitFamilyInfo.Initialize does not dispose the stream returned by LoadFileStream`

```markdown
`RevitFamilyInfo` receives a `Stream LoadFileStream()` delegate and calls it
inside `Initialize()`, but it never disposes the returned stream.

With the `MemoryStream` that `DirectorySource` currently provides this only costs
memory, but it makes the delegate contract dangerous: a provider that returns a
`FileStream` directly (the natural choice for large libraries, to avoid loading
the whole .rfa into memory) leaks an open file handle per family, keeping the
files locked until the finalizers run.

Suggestion: dispose the stream after reading (e.g. wrap the usage in `using`),
and document the ownership rule on the delegate — whichever way the decision
goes, the contract should be explicit.
```

---

# Estado

- [ ] PR 1 abierto (panel vacío — PRIORITARIO)
- [ ] PR 2 abierto (logging)
- [ ] PR 3 abierto (DirectoryFileCache)
- [ ] PR 4 abierto (PreviewStream)
- [ ] PR 5 abierto (BackupRegex)
- [ ] PR 6 abierto (DefaultFamilySources)
- [ ] Issue A abierto (Category)
- [ ] Issue B abierto (InvariantCulture)
- [ ] Issue C abierto (stream sin disponer)

Cuando scotec responda: si piden cambios, las ramas de PR se editan con checkout de la
rama + commit + push (el PR se actualiza solo). Si mergean, sincronizar `develop` y
evaluar rebase de `feature/rfa-cache`.
