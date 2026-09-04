# Pre-release cleanup validation — 2026-09-04

This is a breaking development candidate, not a published release. Current-format validation uses schema 16 and package 6. Earlier validation is retained in history/V1_HARDENING_VALIDATION_2026-09-03.md and does not certify these binaries.

For the subsequent edit/table fixes and currently installed development bundle, see [EDIT_TABLE_VALIDATION.md](EDIT_TABLE_VALIDATION.md). The hashes below describe the earlier cleanup candidate.

## Review order

1. Persistence: canonical state collections, exact versions, serializer/load guards, structural validation, original-envelope preservation.
2. Tags/display rules: removed domain models, filters, naming tokens, persistence and package plumbing; active appearance resolver retained.
3. Templates/title blocks: live source registration, shared checkbox, transient snapshot recipes; None/Right/Bottom and ordinary package geometry.
4. Creation/preview: required specifications, per-detail view assignments, experimental parser, one dialog-close cleanup owner.
5. Documentation and candidate validation: current guidance, builds, regressions, artifacts and installed hashes.

## Acceptance limits

Native Windows checks require a licensed Windows host. Native Mac checks must use fresh fixtures and a full restart into these binaries. Both platforms still require the complete TESTING_AND_RELEASE matrix, including clean-profile installation, all declared Undo policies, metric/imperial PDF output, keyboard/theme/high-DPI behavior, and the 200-sheet/1,000-detail fixture. Historical-project compatibility is outside acceptance.

Nothing was published. The final development bundle was installed and Rhino was fully restarted. Source changes remain in the working tree for review.

## Verified on this computer

- Full solution Release/MacOS build: **0 warnings, 0 errors**.
- Updated Core/Extensibility suite: **350 passed, 0 failed, 0 skipped**.
- `git diff --check`: passed.
- Rhino 8.34.26223.11002 on macOS: **19 focused checks passed** — 11 preview/protected-archive/import rollback checkpoints, five synthetic canvas/tree routing checks, two live-registration/built-in creation checks, and one schema-16 native save/reopen check.
- Live registration followed source page edits for sheet/detail templates. None/Right/Bottom creation succeeded; 11 × 17 inch paper became 279.4 × 431.8 mm in the metric document. Native save/reopen retained four layouts, two registrations, and two managed blocks.
- Package validation passed for exactly 13 allowlisted entries. Assembly versions, MacOS markers, dependency/runtime metadata, and payload checksums agree. Yak emitted its existing assembly/package-name advisory.

The fixture was newly created under `/private/tmp/rlf-cleanup-native/`. Initial attempts exposed test-context limitations: a direct `Write3dmFile` did not name the active native document, and executing modeless mutations inside `RunPythonScript` could not acquire a nested Undo record. The fixture was opened normally and mutation checks were run on Rhino Idle. Headless archive checks run last because their Mac lifecycle can clear ActiveDoc. These are test-harness corrections, not relaxed assertions.

Evidence is in `artifacts/v1-cleanup-evidence/`. Package: `artifacts/v1-cleanup-macos/rhino-layout-foundry-0.1.0-rh8_34-mac.yak`.

Package SHA-256: `049e40d28f420e2a15674de80f9b9d418d517c76d2a7645e94ca0b92df92055e`.

The build, staged bundle, installed bundle, and existing companion's shared assemblies have matching hashes:

| Binary | SHA-256 |
| --- | --- |
| RhinoLayoutFoundry.rhp | `b99cb867bcb874d6ed81073674d13f8cc9e8c673abad260f8c9fda6766c55c13` |
| RhinoLayoutFoundry.Core.dll | `4a57256c03c51aaec34665e026f9c148d10b386ac39cd74de7fd6eef55171434` |
| RhinoLayoutFoundry.UI.dll | `6f7ce60fc79d5ec239afcd3975569387be52884d79c7c853ef68a1cae91d8a03` |
| RhinoLayoutFoundry.Extensibility.dll | `b5ea5810fbf94965421c3ebfcfab4e0a07bffb518ac33226c10eb42057bcb0cd` |
| RhinoLayoutFoundry.deps.json | `810789eb895d04c90ff971466f7bf1bb6c6eac950318608485246216d0c99eeb` |
| RhinoLayoutFoundry.runtimeconfig.json | `4b4546da4fd9175f86782c08db3492baf602ca67a2a6afe53e20a0d47222ab4f` |

## Remaining sign-off

Windows native/build/install checks were not available on this Mac. Physical trackpad/focus behavior, accordion scrolling, dialog-close cancellation under load, dark/light/high-DPI/keyboard behavior, metric/imperial PDF output, every declared Undo policy, clean-profile installation, and the 200-sheet/1,000-detail fixture still require the full release matrix. Synthetic routing and focused native checks do not certify that matrix. Fully quit and reopen Rhino after any later bundle update.
