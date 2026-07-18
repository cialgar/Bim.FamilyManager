# CLAUDE.md — Family Manager Add-in (fork de Bim.FamilyManager)

## Qué es este proyecto

Fork de [Bim.FamilyManager](https://github.com/scotec-Software-Solutions-AB/Bim.FamilyManager) (MIT, scotec Software Solutions AB): add-in de Revit 2025/2026 en .NET 8 para gestionar librerías de familias (.rfa) desde un panel dockable, con drag & drop hacia el modelo.

Objetivo del fork, en orden de prioridad:
1. **Índice SQLite persistente** para librerías grandes (decenas de miles de .rfa) con búsqueda instantánea.
2. **Miniaturas nativas** extraídas del stream `RevitPreview4.0` de cada .rfa (sin preprocesamiento).
3. **Metadatos sin abrir Revit**: versión, categoría, tipos y parámetros leídos de los streams OLE del .rfa.
4. **Fuente remota LibraryRevit** (`Source.LibraryRevit`): catálogo de libraryrevit.com dentro del panel.
5. Correcciones al código upstream (ver `docs/ARCHITECTURE.md` § Deuda técnica) y PRs de vuelta.

El plan completo por fases está en `docs/PLAN.md`. El análisis del código upstream y las decisiones de diseño están en `docs/ARCHITECTURE.md`.

## Flujo de trabajo obligatorio

- **Al iniciar cada sesión**: leer `PROGRESS.md` completo antes de tocar código.
- **Al terminar cada sesión**: actualizar `PROGRESS.md` (qué se hizo, qué quedó pendiente, decisiones tomadas, bloqueos).
- Trabajar por fases según `docs/PLAN.md`. No mezclar fases sin necesidad. Cada fase tiene criterios de aceptación — verificarlos antes de marcarla como completa.
- Diagnóstico de causa raíz antes de aplicar cualquier fix. Nunca workarounds que oculten el problema de fondo.
- Antes de modificar código upstream, entender por qué está escrito así (el código tiene XML docs extensos — leerlos).

## Estructura del repositorio

```
Source/
├── Bim.FamilyManager/                  # Add-in principal, ribbon, registro del pane
├── Bim.FamilyManager.Abstractions/     # Interfaces (IFamilySource, IRevitFamily, IFamilyManager, ViewModels)
├── Bim.FamilyManager.Base/             # Core: FamilyManager, RevitFamily, EStorage, ViewImageWriter
├── Bim.FamilyManager.Source.Directory/ # Fuente: carpetas locales (DirectorySource, DirectoryFileCache)
├── Bim.FamilyManager.Source.AzureStorage/ # Fuente: Azure Blob (plantilla para fuentes remotas)
├── Bim.FamilyManager.Ui/               # UI compartida, FamilyDropHandler (drag & drop)
├── Bim.FamilyManager.Ui.FamilyExplorer/# Panel principal (ViewModels + Views)
└── Bim.FamilyManager.Ui.FamilyNavigator/
Installation/                           # Instalador
```

Proyectos nuevos del fork (se crean en sus fases):
```
Source/
├── Bim.FamilyManager.Rfa/              # Fase 1-2: lectura de streams OLE del .rfa (thumbnails, BasicFileInfo, PartAtom) — SIN dependencia de Revit API
├── Bim.FamilyManager.Index/            # Fase 2: índice SQLite + scanner incremental — SIN dependencia de Revit API
└── Bim.FamilyManager.Source.LibraryRevit/ # Fase 5: fuente remota REST
```

## Reglas técnicas

- **.NET 8 / C# 12**, estilo del upstream: XML docs en inglés en todo miembro público, `sealed` donde aplique, nullable habilitado.
- **Revit API solo desde contextos válidos**: nunca llamar la API de Revit desde threads de fondo o handlers de UI arbitrarios. Para operaciones iniciadas desde WPF usar `ExternalEvent`/`IExternalEventHandler` o los mecanismos que el upstream ya provee. Buscar cómo lo hace `FamilyDropHandler` antes de inventar uno nuevo.
- **`Bim.FamilyManager.Rfa` y `Bim.FamilyManager.Index` NO deben referenciar RevitAPI.dll.** Son librerías puras (OpenMcdf, Microsoft.Data.Sqlite, System.Xml). Esto permite testearlas con xUnit sin Revit — toda la lógica testeable vive ahí.
- Tests: xUnit solo para los proyectos puros. La integración con Revit se valida manualmente en Revit 2026 (smoke test descrito en cada fase del plan).
- Referencias a Revit API: vía los paquetes/props que ya usa el upstream — no agregar DLLs a mano.
- SQLite: `Microsoft.Data.Sqlite` + FTS5. El archivo del índice vive en `%AppData%\FamilyManager\index.db`. Nunca bloquear el hilo de UI durante indexación.
- No romper las interfaces de `Abstractions` — las fuentes nuevas implementan lo existente. Si una interfaz necesita cambiar, documentar el porqué en `docs/ARCHITECTURE.md` y evaluar si el cambio es aceptable para un PR upstream.
- Dependencias NuGet de scotec (`Scotec.Revit`, `Scotec.Revit.Ui`, `Scotec.Queues`): tratarlas como cajas negras hasta completar la verificación de licencias (Fase 0). No copiar código de ellas.

## Comandos

Requisitos confirmados en Fase 0: **SDK .NET 10** (10.0.302 instalado) — el upstream usa
formato de solución `.slnx` y `LangVersion 14.0`; los SDK 8/9 no compilan.

```bash
# Compilar (Revit 2026; sin -p:RevitYear compila para 2025).
# -m:1 es OBLIGATORIO: el markup-compile WPF tiene una carrera en builds multiproceso
# que produce CS2001/BG1002 (archivos .g.cs/.baml ausentes) de forma intermitente.
# Si aun con -m:1 salen CS2001/BG1002: borrar TODOS los obj/ y bin/ bajo Source/ y
# recompilar. Causa: los obj/ NO incluyen RevitYear en su ruta, así que alternar builds con
# distinto RevitYear (p. ej. dotnet test usa 2025 por defecto) corrompe el estado
# incremental del markup-compile WPF. La limpieza parcial lo empeora.
dotnet build Source/Bim.FamilyManager.slnx -c Release -p:RevitYear=2026 -m:1

# Tests (proyectos puros; existirán desde Fase 1)
dotnet test Source/Bim.FamilyManager.slnx

# Publicar: genera Publish/Bim.FamilyManager.addin + Publish/Bim.FamilyManager/ (92 archivos)
dotnet publish Source/Bim.FamilyManager/Bim.FamilyManager.csproj -c Release -p:Platform=x64 -p:RevitYear=2026 --no-build -m:1 -o Publish/Bim.FamilyManager

# Desplegar a Revit 2026 (PowerShell). OJO: copiar el CONTENIDO (\*) — copiar la carpeta
# sobre un destino existente la anida dentro y deja las DLLs viejas en uso.
Copy-Item Publish/Bim.FamilyManager.addin "$env:APPDATA\Autodesk\Revit\Addins\2026" -Force
New-Item -ItemType Directory -Force "$env:APPDATA\Autodesk\Revit\Addins\2026\Bim.FamilyManager" | Out-Null
Copy-Item Publish/Bim.FamilyManager/* "$env:APPDATA\Autodesk\Revit\Addins\2026\Bim.FamilyManager" -Recurse -Force
```

Notas:
- Output de build por proyecto: `Source/<proyecto>/bin/x64/Release/net8.0-windows/`.
- Warnings esperados del upstream: NU1902 (OpenMcdf 3.1.3, vulnerabilidad moderada conocida) y
  CS0219 en código generado por `Scotec.Revit.Isolation.SourceGenerator`.
- `RevitAPI.dll` y demás DLLs de Autodesk no se copian al publish (las provee Revit) — correcto.
- El restore usa `packages/` en la raíz del repo como caché global (NuGet.config del upstream).

## Idioma

- Código, commits y XML docs: **inglés** (consistencia con upstream, facilita PRs).
- `PROGRESS.md`, discusión y documentación interna en `docs/`: **español**.
