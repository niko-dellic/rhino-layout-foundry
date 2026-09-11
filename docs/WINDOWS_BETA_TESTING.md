# Windows matched-pair test handoff

Use `artifacts/pre-beta-clean-break-20260911/matched-Windows.zip` and `ARCHIVE-SHA256.txt`. This is a private protocol-2 source candidate, not a published or beta-qualified release. Earlier beta archives are superseded for this cleanup.

## Installation

Fully quit Rhino. Back up existing development bundles and remove duplicate older load paths from the test profile. Extract the matched archive to a permanent local folder, keeping every DLL/JSON beside both RHP files. In Rhino Options → Plug-ins → Install, select `RhinoLayoutFoundry.rhp`, then `RhinoLayoutFoundry.AI.Rhino.rhp`. Fully quit and reopen Rhino. No SDK or source checkout is required to use the compiled files. This is a manual local installation, not a polished consumer installer.

Match extracted hashes with `assembly-hashes.json` under `Windows-Release`; do not combine files from different candidates. Native Windows install/update/uninstall and dependency isolation remain unverified. No Rhino Package Manager upload is authorized.

For repeatable isolation and removal of manual development load paths, see [Developer installation testing](DEVELOPER_INSTALL_TESTING.md). A manual archive installation is not removed by Yak uninstall.

## Clean-break acceptance

- Verify protocol mismatches refuse a task with instructions to update both components.
- Basic creation must still work. Exercise Drawing Set in-panel: brief → questions → reviewed explicit approval → creation → capture/review → save/reopen.
- Checkpoint 2 must preserve manual titles, ordered messages, pending versus answered questions and resource navigation. Restore must not execute actions or retain approval authority. Saved images become references/placeholders.
- Schema 16, malformed metadata and checkpoint 1 are refused without changing original payloads or Rhino geometry. Valid conversation records still load beside unsupported ones.
- Duplicate sheets/folders through the active hierarchy action and verify caption links; run current schema-17/package-6 round trips and recovery checks.
- Record native Windows version, Rhino build, theme, DPI, candidate hashes and evidence. Use only fresh disposable fixtures.

See [current evidence and blockers](PRE_BETA_CLEAN_BREAK.md). The macOS run used a scripted offline provider; it does not certify live-provider behavior or Windows.

## Guided native pass

1. Create two empty documents. Open Foundry in each and switch between them. Confirm no rows or selections leak between documents. Save, close, reopen, and Save As.
2. Create layouts with None, Right and Bottom title blocks; test A3 and 11 × 17 inch paper, named-view assignments, mixed quantities and captions. Inspect captions below frames and above the bottom block. Register a layout/detail template, modify its source, reopen creation and confirm the source is current.
3. Check List, Thumbnail and Canvas at narrow and wide dock widths. Use dark/light themes and Windows 100/150/200% scaling. Record live-theme-switch behavior; if icons retain the previous theme, verify the documented panel/document reopen or Rhino restart workaround. Check search, empty results, multiselect, clipboard, row drag, view navigation, Tab/Enter/Space/Escape and arrow keys.
4. Verify each operation's stated Undo behavior. Folder and metadata edits must round-trip as declared. Native layout operations marked non-undoable must retain clear warnings and usable recovery paths.
5. Export an ordered mixed-size PDF. Confirm page order, white backgrounds, dimensions, print linework and captions. A failed or cancelled export must preserve an existing destination.
6. Export/import an `.rlf` package with ordinary page geometry, blocks, layers and appearance overrides. Exercise Merge and Replace, conflict choices, cancellation and recovery. Inspect actual native contents, not just counts.
7. Open the supplied `benchmark-200.3dm` fixture; check 200 sheets/1,000 details, scrolling, filtering, selection, zoom/pan, document switching and close. Record retained memory and pauses. The scripted snapshot timings are not a pointer-latency certification.
8. Quit/restart, update/reinstall the candidate, then uninstall from the test profile. The model and recovery files must remain usable. Test without the AI companion first; co-installation is a separate check.

## Repeatable scripts

Run through Rhino `RunPythonScript`, after loading the candidate:

| Script | Required fixture | Checks |
| --- | --- | --- |
| `scripts/rhino-beta-create-fixture.py` | Fresh empty unsaved model | Builds the one-sheet boundary fixture; save with the required filename afterward |
| `scripts/rhino-batch-edit-checks.py` | Fresh unsaved empty model | Active/inactive camera/display edits and compensation |
| `scripts/rhino-current-contracts.py` | Disposable `foundry-boundary-fixture.3dm`, one sheet/detail, mm paper | Live registration, title-block creation, imperial conversion and caption tags |
| `scripts/rhino-beta-pdf-check.py` | Above fixture after creation checks | Ordered PDF and existing-destination protection, including cancellation after final capture and after temporary-file write |
| `scripts/rhino-boundary-checks.py` | Named boundary fixture | Preview cleanup, future metadata, import failure/cancel/Replace recovery |
| `scripts/rhino-beta-metadata-preview-check.py` | Populated named boundary fixture | Four draft-preview failure checkpoints plus nontrivial layer/object appearance recovery, current save/reopen/Save As and protected schema-16 rejection, protected malformed archives |
| `scripts/rhino-beta-empty-import-check.py` | Populated named boundary fixture; **do not save afterward** | Removes fixture layouts, then checks ten empty-destination Merge/Replace recovery cases |
| `scripts/rhino-beta-soak-cycle.py` | Open benchmark with Foundry panel visible | One live-panel timing/memory cycle; close/reopen and repeat ten times, inspect responsiveness independently |
| `scripts/rhino-beta-soak-session.py` | Open `benchmark-200.3dm` with Foundry visible | Load once, then close/reopen ten times; logs weak panel references and after-close retained memory |
| `scripts/rhino-beta-snapshot-check.py` | Disposable `benchmark-paste-save-as.3dm` with one pasted sheet (201/1,005) | Optimized snapshot comparison with explicit native overrides; do not save probe edits |
| `scripts/rhino-canvas-scroll-check.py` | Loaded Foundry | Synthetic scroll/zoom routing; supplement with physical gestures |
| `scripts/rhino-beta-scale-check.py` | Fresh empty unsaved model, or supplied `benchmark-200.3dm` | 200/1,000 fixture, snapshot/refresh/filter timings |
| `scripts/rhino-preview-edit-check.py` | Three named disposable copies described in the script | Real edits before/after previews survive idle and native save/reopen |

Scripts write reports to the OS temporary directory. For the preview-edit checks, observe the native save prompt separately; do not infer it from JSON. The complete acceptance matrix remains in [Testing and release](TESTING_AND_RELEASE.md). Mark unrun scenarios pending, and attach the failing report and reproduction steps for any failure.

## Selection-color regression

- Confirm the bottom toolbar has no selection-color picker.
- Verify fixed selection colors: `#DBDBDB` in light mode and `#444444` in dark mode, including after a live theme switch and restart.
- Check matching List and Canvas hierarchy backgrounds and readable selected text.
- Check the contrasting edge on Thumbnail and Canvas sheet/detail selections against white paper and the board.
- Existing custom selection preferences must have no effect; Rhino's own selected-object color remains unchanged.
