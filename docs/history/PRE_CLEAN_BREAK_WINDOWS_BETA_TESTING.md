# Historical handoff — superseded by protocol 2

# Windows beta candidate handoff

Candidate: **0.1.0-beta.1**, Rhino **8.34+**, .NET 8. The Windows configuration was compiled on macOS; native Windows loading, UI and document behavior are **pending**.

**macOS qualification is still blocked.** The refreshed archive is usable for independent Windows diagnosis, but is not a release-approved handoff. See [Beta qualification](BETA_QUALIFICATION.md) for outstanding UI, Undo, lifecycle, performance and clean-profile installation gates.

The September 11 candidate includes document-workspace cleanup and a toolbar that stacks at narrow widths. Pay particular attention to active/non-active document close, hiding/reopening the panel, shutdown, and repeated resize across the toolbar breakpoint. The final macOS ten-cycle leak regression collected every closed workspace; performance and the full native matrix remain separate gates.

## Package and installation

In the supplied archive, use the `candidate/` folder. In this checkout, use `artifacts/beta-candidate-windows-20260911/rhino-layout-foundry-0.1.0-beta.1-rh8_34-win.yak` from this checkout and its adjacent `PACKAGE-SHA256.txt`. Compare `Get-FileHash -Algorithm SHA256` before installing. Never install the Mac package on Windows.

On a clean test profile, install the local package from PowerShell:

```powershell
& 'C:\Program Files\Rhino 8\System\yak.exe' install 'C:\path\to\rhino-layout-foundry-0.1.0-beta.1-rh8_34-win.yak'
```

Local-file installation is supported by the [official Yak CLI](https://developer.rhino3d.com/en/guides/yak/yak-cli-reference/). Fully quit and reopen Rhino, confirm the version in PlugInManager, and run `LayoutFoundry`. Do not retain a development load path or another older Foundry plugin that supplies different shared assemblies. This package is unsigned and unpublished. Hash verification is integrity checking, not publisher authentication.

Record Windows version, Rhino build, architecture, theme, display scale, package hash, and each result. Run only on new models or disposable copies. Preserve the original project.

## Guided native pass

1. Create two empty documents. Open Foundry in each and switch between them. Confirm no rows or selections leak between documents. Save, close, reopen, and Save As.
2. Create layouts with None, Right and Bottom title blocks; test A3 and 11 × 17 inch paper, named-view assignments, mixed quantities and captions. Inspect captions below frames and above the bottom block. Register a layout/detail template, modify its source, reopen creation and confirm the source is current.
3. Check List, Thumbnail and Canvas at narrow and wide dock widths. Use dark/light themes and Windows 100/150/200% scaling. Switch themes while the panel stays open and confirm icons recolor without restarting. Check search, empty results, multiselect, clipboard, row drag, view navigation, Tab/Enter/Space/Escape and arrow keys.
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
