# Contributing

Read AGENTS.md first. Keep safety fixes, structural changes, and visual changes reviewable separately. Preserve unrelated work in a dirty tree.

## Build configuration

Install the SDK selected by global.json and the .NET 8 runtime. Package versions are centralized in Directory.Packages.props; the product version is in Version.props. Run restore in locked mode normally. When intentionally updating dependencies, regenerate lock files with `dotnet restore RhinoLayoutFoundry.sln --force-evaluate`, review every graph change, and then rerun locked restore.

`FoundryPlatform` is `MacOS`, `Windows`, or `Portable`. MacOS includes native gesture, clipboard, and table adapters and requires Rhino assemblies. Portable exists for hosted CI; it is never a shipping configuration. Keep native dialogs and context menus native.

## Safe local verification

When Rhino is running, isolate outputs:

```sh
dotnet restore RhinoLayoutFoundry.sln --locked-mode
dotnet build RhinoLayoutFoundry.sln --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -p:BaseOutputPath=/private/tmp/foundry-check/
dotnet test tests/RhinoLayoutFoundry.Core.Tests --no-restore --no-build -p:BaseOutputPath=/private/tmp/foundry-check/
git diff --check
```

Use an equivalent absolute temporary directory on Windows. Require zero warnings/errors. Tests must exercise behavior or failure recovery rather than reproduce implementation structure. Host, persistence, import, and preview changes also need licensed Rhino checks; see docs/TESTING_AND_RELEASE.md.

On Macs with no system .NET 8 runtime, the installed Rhino runtime can run testhost explicitly:

```sh
dotnet test tests/RhinoLayoutFoundry.Core.Tests --no-restore --no-build -p:BaseOutputPath=/private/tmp/foundry-check/ -- 'RunConfiguration.DotNetHostPath=/Applications/Rhino 8.app/Contents/Frameworks/RhCore.framework/Versions/A/Resources/dotnet/arm64/dotnet'
```

This is a local fallback, not a different target framework or alternate test runner.

## Install a verified development bundle

From the isolated output's `Debug/net8.0` directory, copy exactly these files into `src/RhinoLayoutFoundry.Rhino/bin/Debug/net8.0`:

- RhinoLayoutFoundry.rhp
- RhinoLayoutFoundry.Core.dll
- RhinoLayoutFoundry.Extensibility.dll
- RhinoLayoutFoundry.UI.dll
- RhinoLayoutFoundry.deps.json
- RhinoLayoutFoundry.runtimeconfig.json

Fully quit Rhino, run `./scripts/install-dev-macos.sh Debug`, compare SHA-256 hashes of source and installed binaries, and reopen Rhino. Installation is not live assembly reload. On Windows use the development load directory documented in README.

## Ownership rules

- Core owns value models, planners, validation, persistence codecs, and host-independent lifecycle policies.
- Host adapters own Rhino resources, UI-thread dispatch, document guards, Undo and compensation.
- UI owns controls and presentation. Application workflows live in FoundryApplicationService; LayoutFoundryUiHost is its composition facade.
- Experimental automation uses the same mutation service. Approvals do not bypass document protection or revision checks.
- Persisted identity is explicit: document runtime serial for the live document, page main viewport ID for sheets, detail viewport ID for details. Every numeric page size carries its unit system.
- Prefer cohesive internal components and shared controls. Do not create a public abstraction merely to reduce a file's line count.

Explain native workarounds, ownership, cancellation, and failure behavior near the code. Update the canonical document when changing a contract, and add changelog entries for observable behavior. Maintain only document schema 16 and package format 6. Do not add historical converters or compatibility aliases. Preserve generic invalid/unsupported-state guards and original-envelope pass-through.
