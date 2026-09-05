# Shared UI dependency

This consumer pins RhinoFoundry.UI, Primitives and (on Mac) MacOS to `0.3.0-preview.1`. The canonical source and contracts are in the sibling `rhino-foundry-ui` repository. Bootstrap packages and their hashes live in `packages/`; do not edit generated DLLs or maintain forked shared controls here.

Build with an explicit `-p:FoundryPlatform=MacOS` or `-p:FoundryPlatform=Windows`, and restore for the same platform first. Use locked restore after updating the lockfile for the target platform. Windows excludes the Mac adapter; Mac native behavior requires the provisioned adapter package. Build into an isolated `BaseOutputPath` while Rhino is open.

Validate a staged bundle before installation:

```sh
python3 scripts/verify-shared-ui.py PATH_TO_BUNDLE MacOS
# or on Windows:
# pwsh -File scripts/verify-shared-ui.ps1 -Directory PATH_TO_BUNDLE -Platform Windows
```

Every installed consumer must carry the matching shared DLL bytes. Upgrade Layout, AI, Block and Maps together, then fully quit and reopen Rhino; existing processes retain loaded assemblies. The development installer checks shared hashes before copying. The AI companion also carries Layout's public integration assemblies, which must match Layout's bundle.

Presentation extraction does not change Rhino document mutations, persistence, native dialogs, domain rules or product-specific rendering. Layout's Core record shapes remain behind adapters. Maps intentionally retains its 34px controls.

Required native sign-off: keyboard/focus/disabled controls, dark/light, high-DPI, table multi-selection and editing, scroll/accordion resizing, canvas wheel versus touchpad pan/pinch, close/reopen and plugin load order. Windows managed compilation is not Windows native sign-off. See `rhino-foundry-ui/docs/VALIDATION.md` for the shared checklist.
