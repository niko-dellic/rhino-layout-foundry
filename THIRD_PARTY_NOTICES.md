# Third-party dependencies

Foundry source is distributed under the repository's MIT license.

The distributable bundle contains Foundry's RHP, Core, UI, and Extensibility assemblies. RhinoCommon, Eto, Microsoft.macOS, System.Drawing, and the .NET runtime are supplied by the installed Rhino/runtime environment; the staging tool does not copy SDK or runtime dependencies into the bundle.

RhinoCommon SDK: Robert McNeel and Associates, copyright 1997–2025 as recorded in the pinned NuGet package metadata. Rhino and related trademarks belong to their owners. Users need a licensed, supported Rhino installation.

The NuGet dependency graph (including transitive build dependencies and test-only xUnit/Test SDK dependencies) is recorded in each project's packages.lock.json. Those packages are not shipped as independent files by Foundry. Review their upstream notices and vulnerability reports when changing the pinned graph. This document is an inventory, not a replacement for upstream licenses.
