# Beta qualification — 2026-09-10

**Status: macOS qualification is blocked; this candidate is not signed off for beta release.**

Candidate **0.1.0-beta.1** is unpublished and unsigned. This is a qualification record, not public-release approval. Windows native testing is reserved for the maintainer. Source changes are uncommitted; the evidence manifest records the base revision and source hashes.

## Fixes found during this pass

- The release allowlist omitted Markdig.dll, although the development installer included it. Both platform packages now include the required Markdown runtime and its license notice.
- The Mac test lock file and Windows lock files were stale relative to shared UI preview.37. Regenerated them without changing the pinned shared UI version.
- Native batch creation failed when caption registration committed a stale detail wrapper. Registration now modifies the current native object's attributes. The regression script verifies the managed-caption tags on every created detail.
- A generated detail could meet the bottom title block with no caption clearance. Built-in creation now reserves a minimum 6 mm beneath frames, preserving larger gaps and leaving registered-source geometry intact. Core zero-spacing tests cover the new behavior.
- A 1,000-detail overview repeatedly enumerated all native objects for every detail. Snapshot capture now enumerates object attributes and eligible layers once. Per-viewport override checks remain per viewport.
- PDF cancellation during the final capture/write boundary could proceed to destination replacement. Cancellation is now checked after capture and immediately before replacement. Internal test callbacks exercise both boundaries deterministically.
- Full inspector snapshots had a second per-detail native-object enumeration. They now cache detail arrays, eligible layers and model attributes for each capture. A native comparison against direct enumeration passed with explicit layer visibility and object display-mode overrides.
- A mounted hierarchy reloaded unchanged native rows because record comparisons included newly allocated collections. Refresh now compares displayed cell values. Updating a row also invalidates cached paper/display-mode text. The same mounted-panel refresh fell from 2,369 ms to 44–63 ms in the two subsequent cycles.

## Theme follow-up

Canvas text and connector halos now use the current canvas background. Panel-owned raster icon frames are regenerated on an appearance change using the existing loaded-panel poll, and replaced images are disposed. Shared hierarchy image caches are bounded to one set per light/dark appearance; row bindings resolve the current set. The source and packages include this presentation-only fix; earlier performance and retention results remain historical evidence and unresolved gates.

## Selection-color follow-up

The bottom toolbar now has a shared Foundry color field beside the view modes. A fresh preference follows the theme: light gray `#DBDBDB` in light mode and dark gray `#444444` in dark mode; the opaque color is remembered per user and feeds List row backgrounds, Thumbnail outlines, Canvas selection outlines and lasso feedback. It does not write Rhino's selected-object color or document metadata. Selected text chooses black or white by luminance. Shared UI preview.40 adds opt-in native Mac row painting while retaining existing table APIs.

Native Dark/Retina observation confirmed the control and a continuous red selected row. The earlier diagnostic build exposed erased cell backgrounds and selection loss during palette refresh; preview.40 avoids native transparent copy fills and preserves selected rows during reload. Complete changed-color/cancel/restart and all-view light/dark acceptance remains pending; the user's active Rhino session was left undisturbed. Core tests pass 454/454 in both configurations; shared UI tests pass 23/23. These changes do not clear the qualification blockers below.

Selection contrast follow-up (2026-09-11): selected Thumbnail sheets and Canvas sheets, details and folder/state frames now have a contrasting keyline behind the accent stroke. Canvas hierarchy selected rows use the same accent and contrasting text, including the label halo. Native visual verification of these changed render paths remains pending restart.

## Automated evidence

Both Release/MacOS and Release/Windows configurations compile with **zero warnings/errors**. The core suite passes **454/454**, with no skips. Windows-configuration tests execute on macOS and do not establish Windows host compatibility.

The dependency vulnerability query reports no known vulnerable packages in the current direct/transitive graph. This is a point-in-time advisory check, not a security guarantee. Notices include shared UI and Markdig licenses. Locked dependency graphs remain in the repository.

Candidate staging verifies assembly versions, platform markers, shared UI package hashes, runtime metadata and exact allowlisted contents. Mac bundles contain the Mac adapter; Windows bundles exclude it. Yak emits its pre-existing assembly-name/package-name advisory; the compiler builds have no warnings.

## Native macOS evidence

Host: Rhino **8.35.26251.13002**, macOS arm64, Retina. The development bundle was installed with Rhino closed and loaded after a full restart. All eight candidate/installed assembly hashes match.

| Check | Result |
| --- | --- |
| Live sheet/detail registration follows source edits | Passed |
| None/Right/Bottom creation, imperial conversion and caption tags | Passed |
| Preview partial-construction ownership and cleanup failure restoration | Passed |
| Merge failures at display modes, named views, layer states, pages, page objects and metadata; cancellation at layer states | Passed |
| Replace cutover rollback and future metadata native save/reopen | Passed |
| Five synthetic canvas/tree scroll and zoom boundaries | Passed |
| Four active/inactive combined-edit and compensation checks | Passed, including final candidate in light mode |
| Ordered native PDF, pre-cancellation and missing-page destination preservation | Passed |
| Final-capture and temporary-write PDF cancellation | Passed; sentinel destination unchanged, no temporary PDF leaked |
| Real edits before/after preview and control | Native save prompts observed; Save/reopen preserved exact object IDs, page counts and Undo recording in all three cases |
| Current metadata native archive save/reopen and Save As | Passed (native file APIs, not complete interactive dialog matrix) |
| Schema-16 migration and intentional save upgrade | Passed |
| Malformed and envelope-mismatched metadata | Mutations rejected, exact original payload preserved through native save/reopen |
| Draft preview failure at page/detail/title-block/appearance checkpoints | Passed; an added test verifies actual layer/object appearance changes before failure and exact native restoration afterward |
| Native close cancellation and interactive Save As cancellation/save/reopen | Passed on disposable clipboard copy; 201 sheets/1,005 details survived reopening |
| Optimized snapshot native override comparison | Passed; explicit layer visibility and object display-mode overrides match direct enumeration, including a negative object control |
| Merge/Replace dependency contents after recovery | Passed for fixture layers/materials/linetypes/dimension styles/hatches and named-view camera/projection contents |
| Empty-destination Merge and Replace | Ten rollback/cancellation cases passed |
| Original Dark appearance | Confirmed selected after testing |
| Exported A3 and 11 × 17 inch media dimensions | Passed within 0.1 mm |
| Four-page rendered PDF inspection | White pages, readable blocks/captions; bottom-block overlap corrected |
| Populated List/Thumbnail/Canvas on 200-sheet/1,000-detail fixture | Opened; thumbnail scrolling and canvas zoom exercised |
| Fresh light/dark panels | Inspected; live theme-switch limitation below |

PDF fixtures contain simple diagnostic geometry, not a production drawing-set fidelity test. Import boundary reports now compare serialized dependency content as well as inventories. Named-view camera/projection values are compared semantically because Rhino regenerates viewport IDs when restoring named views. This diagnostic fixture does not exercise every possible resource or appearance override.

## Scale measurements

Native fixture: 200 A3 sheets, five details per sheet, one model box. Compare `artifacts/beta-qualification/scale-before.json` and `scale-after.json`.

| Measurement | Before | After |
| --- | --- | --- |
| Warm overview capture | 193–202 ms | Typically 42–54 ms, one 109 ms sample |
| Full panel refresh | 202–217 ms | 46–62 ms |
| Filter input processing | Not recorded | 2–20 ms |

These are synchronous handler timings, not frame presentation or physical-input latency. The first recorded capture is after Rhino has opened the fixture, not total cold document-open time. Working set includes Rhino and other loaded plugins; the samples do not prove bounded retained memory. Full scrolling/switch/close memory soak and the >100 ms outlier remain open acceptance items.

The mounted-panel continuation exposed a 2,369 ms refresh, fixed by avoiding unchanged native-row reloads. The earlier two-cycle stop was recovered using Finder to reopen disposable models. Two subsequent ten-cycle runs completed in single Rhino processes: one reexecuted the measurement script each cycle, then a controlled session loaded the harness only once to reduce compilation overhead. Reports: `foundry-beta-soak-resumed.json`, `foundry-beta-soak-session.json` and `foundry-beta-soak-session-closes.json`.

- The controlled session completed **10 measured view-switch/scroll/document-close cycles**. Each after-close sample recorded zero open documents. Retained managed memory after forced collections rose from **130,248,792 to 241,345,712 bytes** (124.2 to 230.2 MiB); working set rose from 2,333,261,824 to 3,498,983,424 bytes. All ten closed panels remained reachable through weak-reference probes, unloaded but not disposed. This is a failed retention gate, not a memory pass. Small diagnostic scripts were additionally compiled after cycles five and ten, so the growth is not attributed wholly to panel retention.
- Controlled-session mounted refresh ranged from 39 to 133 ms; captures reached 135 ms. View switches reached 98 ms for Thumbnail, 396 ms for Canvas and 163 ms returning to List. The separate repeated-script run included a 163 ms capture. The original 109 ms outlier is reproducible as a class of >100 ms stalls; some samples coincide with collections and some do not.
- The new full-snapshot optimization was installed with Rhino closed and tested after restart. Three subsequent samples (two document opens, the third on the same open document) measured Canvas at **121–139 ms**, down from 329–396 ms in the controlled prior session. Thumbnail was 66–104 ms, returning to List 151–160 ms, refresh 41–93 ms and capture peaked at 125 ms. These improvements do **not** meet the 50 ms common-input budget. No ten-cycle retention pass is claimed for this changed binary.
- Explicitly disposing only closed-document panels released about 14 MB but did not remove their weak-reference targets. A further diagnostic timer cleanup also left the panels reachable. Neither experiment is promoted into a speculative production lifecycle fix. `dotnet-dump` heap capture was rejected by macOS `task_for_pid` entitlements; no security settings were changed. The retention root remains unresolved.
- No complete cold-open-to-presented-hierarchy/board measurement or physical input-to-frame latency certification exists.

Native UI observations additionally cover List search, Escape returning focus to content, arrow and Shift-arrow List selection, clipboard paste, native close/save cancellation and interactive Save As/reopen. Thumbnail selection by pointer and arrows was observed in Dark mode. These are partial observations, not a completed all-mode/all-theme keyboard, clipboard, drag and resize matrix. The earlier menu/accessibility issue is historical evidence, not the current reason to stop qualification.

## Known limitations and remaining gates

- Native Windows acceptance is pending; use [Windows beta testing](WINDOWS_BETA_TESTING.md).
- The reported light-mode canvas label halo and live icon recoloring defects are fixed in the refreshed candidate. Native Retina checks covered light → dark → light canvas switching, selected detail labels, toolbar/search/brand icons, and live List-row recoloring. Original Dark appearance was restored. Other UI matrix entries remain pending.
- Full keyboard/clipboard/multiselection/drag/resize coverage in all three modes and both themes remains incomplete. Native reopen automation recovered; partial observations are listed above.
- The completed ten-cycle run failed the retention gate. Common-input timing and unexplained >100 ms stalls remain blockers. Both snapshot optimizations improved timings, but this is not a blanket performance pass.
- Every enabled Undo/Redo family and the remaining preview/cancellation/document-switch combinations still need the full matrix in TESTING_AND_RELEASE.md. Interactive Save As cancellation/save/reopen and nontrivial preview appearance restoration now have passing evidence. Newly invalidated paper/display-mode labels still need an interactive mutation regression.
- Clean-profile native Yak install/restart/update/uninstall without development dependencies remains unverified. A **Yak.Core component-only** install/update/remove test passed in a unique temporary package directory and left no files. Its update baseline was a synthetic beta.0 manifest around candidate binaries; it does not establish a real prior-release upgrade or Rhino host loading. Existing development settings/bundles were not displaced for that test.
- Packages are hash-verified, not publisher-signed. Signing and approval of exact package hashes remain publication decisions. No Yak push or GitHub release was performed.

The recent AI drawing-set readiness exercises are separate evidence for the companion and do not substitute for this public-plugin qualification.

## Evidence and handoff

Local evidence lives in `artifacts/beta-qualification/`: test TRX files, native JSON reports, before/after scale measurements, PDF export and dimensional checks, dependency advisory report, installed hashes and package-verifier checks. Diagnostic models are under `/private/tmp/foundry-beta-final/` and `/private/tmp/foundry-beta-native/`; a handoff copy of the scale fixture is retained with the evidence.

Refreshed diagnostic packages are in `artifacts/beta-candidate-macos-final/` and `artifacts/beta-candidate-windows-final/`, each with `PACKAGE-SHA256.txt` and `SHA256SUMS`. Use those hashes to identify tested distributions. Further source changes require a new build and relevant reruns; historical evidence is not a blanket pass for changed binaries.

The Windows archive is refreshed for independent testing, but remains a **diagnostic handoff with macOS blockers**, not the post-qualification approval requested by the release plan. No public API or persistence-schema change was introduced during this continuation.
