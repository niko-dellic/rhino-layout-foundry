# Testing and release

A public release requires a reviewed candidate on licensed Rhino for **both Windows and macOS**. Hosted CI only proves compilation and host-independent contracts. Never infer native Undo, capture, persistence, or UI correctness from those tests.

## Automated gate

```sh
dotnet restore RhinoLayoutFoundry.sln --locked-mode
dotnet build RhinoLayoutFoundry.sln -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test tests/RhinoLayoutFoundry.Core.Tests -c Release --no-build --logger trx
git diff --check
```

Use an isolated BaseOutputPath if Rhino has loaded an existing development output. Require zero warnings/errors and no failing or skipped tests. The suite includes domain planners and serialization, protected metadata classification, compensation ordering/failure continuation, preview coalescing/stale-result rejection, and automation allow-list, expiry, revisions, plan freezing, and token reuse.

CI restores lock files and runs Windows plus portable Mac builds. Native Mac release candidates require installed Rhino references. Missing native references must fail a MacOS build; Portable assemblies must fail candidate staging. Review vulnerability reports for the locked dependency graph before release; the workflow does not currently certify them automatically.

## Build a candidate

Set Version.props deliberately; do not overwrite an already published version. Build `Release` with explicit `FoundryPlatform=MacOS` or `Windows`. Both SDK and .NET 8 runtime must be installed. A provisioned release host also needs PowerShell and `FOUNDRY_YAK` pointing to its installed Yak executable.

The manually dispatched release-candidate workflow builds, tests, stages, and uploads artifacts. It never publishes. Self-hosted runners use labels `foundry-release` plus `macOS` or `Windows`; run only trusted revisions there.

For a local Mac candidate:

```sh
dotnet build RhinoLayoutFoundry.sln -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -p:FoundryPlatform=MacOS -p:BaseOutputPath=/private/tmp/foundry-candidate/
dotnet /private/tmp/foundry-candidate/Release/net8.0/Foundry.ReleaseCheck.dll "$PWD" /private/tmp/foundry-candidate/Release/net8.0 /private/tmp/foundry-staged MacOS
cd /private/tmp/foundry-staged
"/Applications/Rhino 8.app/Contents/Resources/bin/yak" build --platform mac
dotnet /private/tmp/foundry-candidate/Release/net8.0/Foundry.ReleaseCheck.dll --verify-package /absolute/path/to/rhino-layout-foundry /private/tmp/foundry-staged/rhino-layout-foundry-VERSION-rh8_34-mac.yak MacOS
```

Use a fresh staging directory each time. On Windows use equivalent absolute paths and `Windows`/`win`. The staging tool checks all four assembly versions and platform markers, net8.0 runtime metadata, and the dependency manifest. It copies only the six runtime files, manifest, license, user/recovery documentation, and SHA256SUMS. Inspect the resulting Yak ZIP and verify package hashes after Yak finishes. SDK/runtime/test assemblies must not be bundled. The post-package verifier rejects extra/missing/duplicate files and validates every payload checksum. Yak rewrites the manifest, so it is checked separately and covered by the final package hash. Replace VERSION and repository paths in the example above.

## Native regression scripts

Fully restart Rhino with the candidate installed. Open a **disposable copy** of a representative document named `foundry-boundary-fixture.3dm`. Include model views, layouts/details, named resources and title-block/page objects; never rename your original model for this purpose.

Run `scripts/rhino-boundary-checks.py` using Rhino's `RunPythonScript`. It exercises partial preview construction, cleanup failure, future-metadata archive pass-through, Merge failures at display modes/named views/layer states/page/page objects/metadata, injected cancellation, and Replace cutover recovery. Run `scripts/rhino-current-contracts.py` once on a fresh single-sheet/detail boundary fixture for live registration and built-in/imperial creation. It schedules modeless mutations after RunPythonScript releases its Undo record. The private checkpoint seams are internal test hooks, not public contracts. The import check compares native resource inventories; it does not prove every object's appearance or full before/after content equivalence. Recovery packages are intentionally retained in the OS temporary directory.

For the real-edit regression, start a fresh Rhino session and prepare three disposable copies named `foundry-preview-control.3dm`, `foundry-preview-before.3dm`, and `foundry-preview-after.3dm`. For each copy:

1. Open it and run `scripts/rhino-preview-edit-check.py` to create a real edit, with the corresponding preview scenario.
2. Let Rhino return to idle, then run the script again. It checks object survival, page count and Undo recording; Windows also checks Modified.
3. Close the document through Rhino's native UI. Observe and record its Edited indicator and save prompt, then choose Save on this disposable copy.
4. Reopen the saved copy and run the script a third time. The exact point ID must survive and temporary pages must be absent. Close this saved copy afterward.

The script writes one JSON report per case to the OS temporary directory. A script pass does not certify the native save prompt; record that observation separately. On Mac, `RhinoDoc.Modified` is diagnostic only: assigning it does not establish native clean/dirty state. See the [investigation and McNeel reference](PREVIEW_SAFETY_VALIDATION.md). Do not run the test against original work files or reuse a case with a different fixture within the same Rhino session.

Additional fixtures and the platform matrix below remain required, including cancellation, overwritten dependency content, per-viewport attribute equivalence, empty destinations and each provider construction checkpoint.

## Licensed host acceptance

Run on disposable copies or newly created test documents. Preserve source fixtures. Record exact commit, package SHA-256, Rhino build, OS, architecture, theme, display scale, result, and evidence path. Mark unrun checks **pending**, never passed.

| Area | Required scenarios and success condition |
| --- | --- |
| Metadata | Empty/current state save/reopen and Save As; supported schema 16; future version; malformed JSON; missing/null collections; envelope/payload mismatch. Opening/reading does not change state. Protected metadata remains preserved on save and Foundry rejects changes. |
| Preview ownership | Inject failure after page acquisition, after a detail, and during appearance/title-block work. No temporary pages/definitions remain; original appearance and Undo-recording state survive. A cleanup failure does not prevent other restoration and is reported. |
| Preview lifecycle | Cancel/close while rendering; make a real edit immediately afterward; run several idle turns. The real edit remains unsaved in the host (native Edited/save prompt on Mac; Modified plus save prompt on Windows). Switch/close documents and confirm stale images are not presented. |
| Import | Merge and Replace, including an empty destination. Inject failure after each dependency family, first/last page, page-space objects, replacement cutover, and metadata update. Cancel at safe boundaries. Compare named views, layer states, definitions, materials, line/hatch/dimension styles, pages, and metadata to before-state. Verify recovery diagnostics and usable recovery package. |
| Undo | For every enabled operation family, verify the declared policy. Where Undo is supported, one Undo/Redo round-trips all affected native content and metadata. Non-undoable operations must disclose that limitation and pass compensation/recovery checks. |
| Document lifecycle | New/open/save/Save As/close; two documents; active/non-active close; repeated panel open; shutdown and restart. No state, event, or image leakage across documents. |
| Units and PDF | Equivalent millimeter/inch paper sizes and detail scales; explicit 25.4× regressions; title-block placement; ordered PDF pages, white paper, correct physical dimensions, failure/cancel leaves no partial destination. |
| UI | List/Thumbnail/Canvas, empty and populated, narrow/docked/floating, dark/light, Retina and Windows 100/150/200%; Tab, Enter, Space, Escape, arrows; native clipboard and multi-selection. |
| Scale | Deterministic 200-sheet/1,000-detail fixture. Record cold/warm open, input latency and retained memory across scrolling, document switching and close. Target interactive hierarchy/board within 1 s and common input within 50 ms. No unexplained main-thread block above 100 ms. |
| Package | Clean-profile install, plug-in loading, sample workflow, save/reopen, restart, update, uninstall. Check installed RHP/UI/Core/Extensibility SHA-256 against staged artifacts. |

The existing RHINO_SMOKE_TEST.md and UI_VISUAL_BASELINES.md provide detailed interaction scenarios. Record discrepancies against this candidate; historical completed checks do not certify newly changed binaries.

For creation/edit settings, leave the expanded accordion idle, then scroll in both directions. Check that layout notifications settle after opening and after resizing; scrolling alone must not continually resize the content. Widen and narrow the settings pane, collapse/reopen sections, and verify that fields still fit. See [the macOS scroll investigation](SETTINGS_SCROLL_VALIDATION.md) for the observed regression and focused validation.

Run `scripts/rhino-canvas-scroll-check.py` through Rhino's RunPythonScript to check canvas/tree gesture boundaries on an isolated, undisplayed canvas. It tests short, overflowing, empty and hidden trees, prevention of tree-to-zoom fallthrough, and ordinary mouse-wheel zoom. The corrected Mac build passed all five checks on 2026-09-04; the previous build failed the short-tree boundary, empty-tree boundary and zoom-fallthrough checks. This does not synthesize physical gestures: after restart, manually verify two-finger panning below and beside the tree, scrolling over overflowing tree rows, pinch zoom, and behavior before/after focus and view changes. Local comparison evidence is in `artifacts/canvas-scroll-2026-09-04/`.

## Sign-off and publication

- [ ] Automated gate passes for the candidate commit.
- [ ] Native Windows candidate passes the table above.
- [ ] Native Mac candidate passes the table above.
- [ ] Recovery, unsupported metadata and fault-injection checks pass.
- [ ] Dependency review, package-content inspection and hashes are recorded.
- [ ] README, changelog, notices and known limitations match the candidate.
- [ ] No unresolved critical/high safety issue remains.
- [ ] Maintainer approves the exact package hashes for publication.

After approval, publish immutable GitHub/Yak artifacts and verify Package Manager discovery/install on both platforms. Preserve the packages and evidence. A failed release requires a new version; never replace an existing published package. Uninstall must not remove document metadata or user recovery files.

## Current-format cleanup acceptance

Use fresh fixtures created by this candidate. Schema 16/package 6 are required; historical-project compatibility is outside acceptance.

- [ ] Register/unregister live sheets and details; change the source, reopen creation, and confirm the preview follows it. Delete a source and confirm it disappears. Folders expose no template checkbox.
- [ ] Create None/Right/Bottom blocks, edit project fields, append per-sheet revisions, and save/reopen. Ordinary block instances must remain ordinary page geometry through package round trips.
- [ ] Exercise canonical per-detail creation through UI and the experimental companion, including absent views, wrong assignment counts, stale plans, cancellation, and single-use approval.
- [ ] Cancel/close creation while captures are queued and in progress. Confirm the dialog's cleanup barrier completes and temporary resources are gone.
- [ ] Repeat the accordion scrolling and canvas/tree gesture checks on this build, including focus changes, dark/light, and high DPI.

## Edit and table regressions

On a fresh unsaved diagnostic model, run `scripts/rhino-batch-edit-checks.py` through
Rhino RunPythonScript. It schedules execution on Idle and creates its own layouts
and named view. Never run it in a user's project. It checks combined sheet mode /
named-view edits and an injected failure after the first detail edit, for both
active and inactive details. Compare native cameras, display modes, and metadata.
The JSON report is `foundry-batch-edit-checks.json` in the OS temp directory.
`CANDIDATE_HOST` can point to an isolated RHP for development checks; the default
uses the installed bundle after restarting Rhino.

For table interaction, drag from layout/folder names. Confirm short/slow starts,
single and multiple selected rows, insertion before/after siblings, folder drops,
Escape cancellation, and a subsequent drag after cancellation. Property cells
remain editors; dragging a detail must not move its parent layout. Verify common
row height/font/striping in hierarchy, appearance rules, and creation review;
native selection colors must take priority. Repeat with keyboard selection,
light/dark themes, and Retina/high-DPI on both platforms.
