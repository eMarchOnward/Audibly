# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

!include soul.md

## Project Overview

Audibly is a WinUI 3 Windows desktop audiobook player supporting `.m4b`, `.mp3`, and `.m4a` files. It targets .NET 8.0 on x64/x86/ARM64 and is distributed as both an MSIX package (Microsoft Store) and an unpackaged ZIP.

## Build and Development

**Requirements**: Visual Studio 2022, .NET 8 SDK, Windows App SDK 1.7

**Build** (from solution root):
```
msbuild Audibly.sln /p:Configuration=Release /p:Platform=x64
```

**Restore packages**:
```
msbuild Audibly.sln /t:Restore /p:Configuration=Release
```

**Build unpackaged** (no MSIX, for ZIP distribution):
```
msbuild Audibly.sln /t:Publish /p:WindowsPackageType=None /p:Configuration=Release /p:Platform=x64
```

**Package and publish** (creates GitHub release):
```powershell
.\package-and-publish.ps1
```

**EF Core migrations** (run from repo root):
```
dotnet ef migrations add <MigrationName> --project Audibly.Repository --startup-project Audibly.App
dotnet ef database update --project Audibly.Repository --startup-project Audibly.App
```

There are no automated tests — manual QA checklists are in `things-to-test.md`.

## Architecture

Three-project layered solution:

```
Audibly.App  (WinUI 3 executable)
    └──> Audibly.Repository  (EF Core + SQLite data access)
              └──> Audibly.Models  (domain POCOs)
```

**`Audibly.Models`** — Pure C# POCOs: `Audiobook`, `ChapterInfo`, `SourceFile`, `Bookmark`, `Tag`, `ImportedAudiobook` (migration DTO). `DbObject` is the base class with a `Guid Id`.

**`Audibly.Repository`** — `AudiblyContext` (EF Core DbContext with SQLite), repository interfaces (`IAudiblyRepository`, `IAudiobookRepository`, `IBookmarkRepository`), and SQL implementations. Each sub-repository instantiates its own `AudiblyContext` per call.

**`Audibly.App`** — MVVM app with these layers:
- `Views/` — XAML pages and content dialogs
- `ViewModels/` — Extend `BindableBase` (custom `INotifyPropertyChanged`); **not** CommunityToolkit's `ObservableObject`
- `Services/` — Business logic (`FileImportService`, `AppDataService`, `BookmarkService`, `DialogService`, etc.)
- `UserControls/` — Reusable WinUI controls
- `Helpers/` — Static utilities (`UserSettings`, `Constants`, `ThemeHelper`, `TitleBarHelper`, `WindowHelper`)
- `Extensions/` — Extension methods on domain/WinRT types

## Key Patterns

**Singleton ViewModels**: `App.ViewModel` (`MainViewModel`) and `App.PlayerViewModel` (`PlayerViewModel`) are static singletons on the `App` class, accessed directly throughout the codebase — not via DI.

**Repository access**: `App.Repository` is the `IAudiblyRepository` singleton set at startup in `App.xaml.cs`.

**User settings**: All preferences stored in `Windows.Storage.ApplicationData.Current.LocalSettings`, wrapped by the static `UserSettings` class in `Helpers/`.

**Audio playback**: `PlayerViewModel` owns a `Windows.Media.Playback.MediaPlayer` instance. Position updates fire every 500 ms; position is persisted every 10 seconds using a dirty flag. Multi-file audiobooks are handled by switching `SourceFile` entries as playback progresses.

**File import pipeline**: `FileImportService` uses the ATL library (`z440.atl.core`) to read audio metadata and chapters. `AppDataService` uses ImageSharp to crop cover art and generate thumbnails. Imports use `CancellationTokenSource` and async progress callbacks.

**Data migration**: A custom migration system (separate from EF migrations) handles the v2.1 schema break: exports old DB to a `.audibly` JSON file, drops and recreates via EF migrations, then re-imports.

**Single-instance + file activation**: `AppInstance.FindOrRegisterForKey("main")` enforces single-instance. The app registers as a handler for `.m4b`/`.mp3`/`.m4a` so opening a file from Explorer activates the running instance.

**Theming**: Mica backdrop via `MicaController`. Theme changes (light/dark/system) require an app restart.

## Key Dependencies

| Package | Purpose |
|---|---|
| `Microsoft.WindowsAppSDK` 1.7 | WinUI 3 runtime |
| `CommunityToolkit.Mvvm` | Commands, observable helpers |
| `CommunityToolkit.WinUI.*` | Controls (Settings cards, TokenizingTextBox, Media, Triggers) |
| `Microsoft.EntityFrameworkCore.Sqlite` | ORM + SQLite persistence |
| `z440.atl.core` (ATL) | Audio tag and chapter metadata extraction |
| `SixLabors.ImageSharp` | Cover art processing |
| `AutoMapper` | Maps `ATL.ChapterInfo` → `Audibly.Models.ChapterInfo` |
| `Sentry` | Crash reporting (Release builds only) |
| `Dapper` | Legacy DB reads during data migration |

## CI/CD

GitHub Actions (`.github/workflows/dotnet-desktop.yml`) triggers on push/PR to `main`. Pipeline steps: update version in `Package.appxmanifest` → restore → build MSIX (x64) → build unpackaged → create GitHub Release. No automated test step exists.
