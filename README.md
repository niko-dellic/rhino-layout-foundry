# Rhino Layout Foundry

An open-source layout manager for Rhino. Foundry organizes folders, layout sheets, detail viewports, reusable templates, and appearance states through List, Thumbnail, and Canvas views.

**Status: pre-release.** The repository includes v1 hardening and candidate tooling. Public Windows and macOS support requires the host checks in [Testing and release](docs/TESTING_AND_RELEASE.md). A passing core test suite is not a Rhino compatibility certificate.

## Requirements

- Target host for candidate validation: Rhino 8.34 or later, Windows or macOS, running .NET 8. The SDK is pinned to RhinoCommon 8.34; older Rhino versions are not advertised as supported.
- Building: the .NET SDK selected by [global.json](global.json), plus the .NET 8 runtime for tests and tools. C# 12 and `net8.0` are explicit build settings.
- Native Mac builds need the Eto.macOS and Microsoft.macOS assemblies from the installed Rhino application.

## Current capabilities

- Nested folders and batch hierarchy operations, shared selection, filtering, sorting, copy/paste, and notes.
- Layout creation and editing with naming, page sizes, detail assignments, and managed title blocks.
- Document templates and reusable appearance states with per-detail layer/display rules.
- List, Thumbnail, and spatial Canvas views with lazy previews.
- Ordered PDF output and portable `.rlf` package import/export with dependency conflict choices.
- An [experimental companion API](docs/AUTOMATION_SDK.md) for trusted in-process integrations.

Foundry calls a Rhino page layout a **sheet**; a **detail** is its model-space viewport. **Folders** are Foundry metadata, while Rhino's native layout tabs remain flat.

## Known limitations

Some Rhino layout operations are not natively undoable. Foundry uses validation, compensating rollback, and recovery packages where applicable; read each operation's warning. Do not assume one Undo will reverse layout creation, deletion, rename, or package replacement. Platform-specific Undo verification remains a release gate.

Unsupported or malformed Foundry metadata is protected from Foundry edits. Recoverable archive envelopes are preserved on save. Supported old metadata is migrated in memory; a successful intentional edit causes the current schema to be written. See [Recovery](docs/RECOVERY.md).

Live previews temporarily create Rhino page content. Canceling a preview can leave an unsaved-change indicator: Foundry deliberately does not clear that flag after deferred native events, because doing so could hide a real edit.

PDF and package work run through Rhino's UI thread. Cancellation is checked at safe boundaries, not during every native call. Large-file responsiveness and long-operation behavior must pass the release fixture checks.

## Installing a published release

When a release is published, use Rhino's `PackageManager` to install `rhino-layout-foundry`, then fully restart Rhino and run `LayoutFoundry`. For a supplied candidate, use its matching Windows or Mac `.yak` package and follow the clean-profile checks in [Testing and release](docs/TESTING_AND_RELEASE.md). A local candidate is not a published release.

## Build and development install

```sh
dotnet --version
dotnet --list-runtimes
dotnet restore RhinoLayoutFoundry.sln --locked-mode
dotnet build RhinoLayoutFoundry.sln --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test tests/RhinoLayoutFoundry.Core.Tests --no-restore --no-build
```

The default platform is MacOS on macOS and Windows on Windows. `-p:FoundryPlatform=Portable` supports unprovisioned build/test machines and cannot be packaged for distribution. MacOS builds fail when native references are missing; use `RhinoMacResources` to specify a nonstandard Rhino installation.

Fully quit Rhino before building into its usual load directory or installing. On macOS:

```sh
./scripts/install-dev-macos.sh Debug
open -a "Rhino 8"
```

The script copies a complete bundle from `src/RhinoLayoutFoundry.Rhino/bin/Debug/net8.0` to `~/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/RhinoLayoutFoundry.rhp`. It also synchronizes shared assemblies in an existing companion development bundle. Run `LayoutFoundry` in Rhino; `LayoutFoundryObserver` opens Canvas mode.

When Rhino is running, build to an isolated `BaseOutputPath`; follow [Contributing](CONTRIBUTING.md) before assembling and installing. Fully quit and reopen Rhino after a bundle update.

On Windows, build with Rhino closed, set `RHINO_PACKAGE_DIRS` to the host project's `bin/Debug/net8.0` directory, and launch Rhino using `/netcore`. Check `PlugInManager`, then run `LayoutFoundry`.

## Documentation ownership

| Document | Authority |
| --- | --- |
| [Contributing](CONTRIBUTING.md) | Setup, change boundaries, validation and development installation |
| [Architecture](docs/ARCHITECTURE.md) | Current ownership, interfaces, persistence and mutation flow |
| [Testing and release](docs/TESTING_AND_RELEASE.md) | Automated checks, live host sign-off and packaging |
| [Recovery](docs/RECOVERY.md) | Protected metadata and failed-import recovery |
| [Automation SDK](docs/AUTOMATION_SDK.md) | Experimental companion contracts and trust model |
| [Development status](docs/DEVELOPMENT_STATUS.md) | Current evidence and outstanding release gates |
| [Product specification](docs/PRODUCT_SPEC.md) | Product intent; not evidence that every feature has shipped |
| [Changelog](CHANGELOG.md) | User-visible changes |

Historical milestone and architecture notes live in `docs/history` and are not current implementation contracts. [AGENTS.md](AGENTS.md) governs contributor safety and the Foundry design system.

Licensed under [MIT](LICENSE). See [dependency notices](THIRD_PARTY_NOTICES.md).
