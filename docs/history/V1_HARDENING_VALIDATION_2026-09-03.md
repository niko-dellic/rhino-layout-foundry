# V1 hardening validation — 2026-09-03

Status: implementation ready for review; **public release blocked on remaining native validation**.

This evidence applies to the working-tree hardening changes based on `3b6ca6b8ab62ad9bf43effa00570d2b37074c1f8`, not an immutable tagged release. Version remains `0.1.0`. Commit the reviewed changes and rebuild the publication candidate before sign-off.

## Review order

1. Persistence: `DocumentStateLoadResult`, serializer validation, `DocumentStateStore`, mutation guards and protected-state UI diagnostic.
2. Preview: `CompensationJournal`, `RhinoPreviewSession`, thumbnail provider ownership and removal of draft and named-view modified-flag resets.
3. Import: `RhinoImportTransaction` and package service recovery, touched layer/object attribute snapshots, cancellation boundaries and retained recovery packages.
4. Structure: mutation executor operation-family files; instance application service; extracted creation galleries/session state; shared latest-preview scheduler; removal of the unused batch session and alternate test harness. Project boundaries and existing migrations remain intact.
5. Contracts/docs/tooling: experimental automation registry tests; canonical README/architecture/contributor/recovery guidance; SDK/dependency/version policy; platform validation and candidate ZIP verification.

## Automated results

- Native Mac Release solution build: **0 warnings, 0 errors**.
- Core/Extensibility regression suite: **353 passed, 0 failed, 0 skipped**.
- Locked restore succeeded locally. Vulnerability auditing was disabled for the offline local restore and remains a release gate.
- `git diff --check`: passed.
- Portable build and staging rejection exercised; missing Mac native references rejected by the build guard.
- Package allowlist, all payload checksums, manifest version/platform and final ZIP SHA-256 verified. A ZIP with an extra DLL is rejected.

Environment: macOS 26.6.2, arm64; .NET SDK 10.0.103; native Rhino runtime .NET 8.0.14; Rhino 8.34.26223.11002. Isolated build output: `/private/tmp/rlf-v1-verified/Release/net8.0`. Tests used Rhino's bundled .NET 8 runtime because the system SDK installation does not include that runtime.

## Native Mac results

Executed on a disposable copy containing four sheets and ten details, preserving the original model:

- Partial preview construction removed its owned page and restored Undo recording.
- Injected cleanup failure was reported and did not prevent Undo-recording restoration.
- Unsupported schema 99 stayed protected, and its exact original JSON payload survived native save/reopen.
- Merge failure at display modes, named views, layer states, page creation, page objects and metadata restored the checked resource inventories and retained a recovery package.
- Injected cancellation after layer states followed the same recovery path.
- Replace failure after cutover restored original layouts through the recovery package.
- The real model edit survived preview cleanup. The original dirty-baseline assertion failed on macOS; the [September 4 investigation](PREVIEW_SAFETY_VALIDATION.md) resolved it as a platform-inappropriate test assumption. Native save protection and saved edits passed in control, before-preview, and after-preview cases.

Native result on September 3: **11 passing checks and one subsequently invalidated Mac modified-flag assertion**. On September 4, the corrected three-case test and native save/reopen sign-off passed. See [Preview safety validation](PREVIEW_SAFETY_VALIDATION.md) for the evidence and platform-specific test contract. Historical September 3 reports remain in `artifacts/foundry-boundary-checks.json` and `artifacts/foundry-preview-edit-check.json`; the corrected September 4 reports are in `artifacts/preview-safety-2026-09-04`. See [script instructions and scope](TESTING_AND_RELEASE.md#native-regression-scripts). The inventory checks do not certify full appearance/content equivalence for every dependency type. The original document was reopened and disposable changes discarded.

## Candidate and installation

The staged package is `artifacts/v1-release-candidate-macos/rhino-layout-foundry-0.1.0-rh8_34-mac.yak`. It contains exactly thirteen allowlisted entries, including six runtime files. Test/SDK/runtime libraries are excluded. Yak emits a naming advisory because the plugin assembly is `RhinoLayoutFoundry` and its package slug is `rhino-layout-foundry`; version and platform checks pass.

Package SHA-256: `f89eeb9333ed3cd7e8621e5a863b1614aee75fef6463a6a14ee781afa5b92ce9`.

The local development bundle and existing companion's shared assemblies were updated. Rhino was fully quit and restarted. Installed RHP/Core/UI/Extensibility hashes match the staged binaries:

| Binary | SHA-256 |
| --- | --- |
| RhinoLayoutFoundry.rhp | `132b15371cc11ab259b5fe62bd4936725f65f04f15382a1db24faac25459899f` |
| RhinoLayoutFoundry.Core.dll | `9c1f2bf07661ba15336d8a7856efbaef0f6f58a2f3a0f0493ca68e93f602491a` |
| RhinoLayoutFoundry.UI.dll | `62f0aa36c1e873d8c8cca57259d69501009e637e86df484ad49a786b64a0a312` |
| RhinoLayoutFoundry.Extensibility.dll | `b544e6113184c932e9baca65ac59c848189de7968fc8f1f8b9a2e5b8bd5d947e` |

## Remaining public-release gates

- Licensed Windows candidate build, native tests and clean-profile installation; no Windows host was available in this session.
- Full Mac and Windows matrix: Save As, historical/corrupt native archives, document switching/cancellation, every declared Undo policy, preview faults within each provider construction stage, overwrite-content and per-viewport attribute equivalence, and empty-destination import.
- Metric/imperial creation and PDF output; keyboard/focus, dark/light, and high-DPI UI sign-off after the extracted controls.
- 200-sheet/1,000-detail performance and retained-memory fixture.
- Clean-profile Yak install/update/uninstall on both platforms and dependency vulnerability review.
- Review/commit, final release version decision, and approval of immutable package hashes.

Use [Testing and release](TESTING_AND_RELEASE.md) for sign-off. Nothing was published. Fully quit and reopen Rhino after any subsequent bundle update.
