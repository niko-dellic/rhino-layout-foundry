# Edit and table fixes — 2026-09-04

This development bundle follows the pre-release cleanup candidate. It is not a
published release or a completed Windows/macOS release sign-off.

## Follow-up: live drag-selection painting

Mac cells now remain transparent and AppKit owns alternating row backgrounds and
live selection. Opaque Eto zebra and semantic cell fills were covering AppKit's
highlight during mouse tracking. Folder/drop and changed-value cell backgrounds
also respect this policy; changed values retain their bold warning text. Picking
objects for an appearance state now selects the actual corresponding native rows.
The attempted notification/reload workaround was removed.

Tested the installed controls in restarted Rhino 8.34.26223.11002, in dark mode:

| Surface | Downward drag | Upward drag | Native event before release |
| --- | --- | --- | --- |
| Main table | 6 sheets | 4 sheets | LeftMouseDragged |
| Appearance layer table | 9 layers | 6 layers | LeftMouseDragged |
| Creation review | 6 draft rows | 4 draft rows | LeftMouseDragged |

Each surface displayed full-width gray selection. Native selection-change records
show no opaque selected-cell backgrounds while the mouse is held, with native
striping enabled. Appearance and creation checks covered 36 and 66 formatted cells
respectively. Both dialogs were cancelled without saving/applying. Tests used six
empty pages and disposable layers in a fresh unsaved model, not a user project.

`scripts/rhino-table-selection-check.py` runs the three actual installed controls,
one surface per invocation. Its report supplements visual checks; core tests alone
cannot verify AppKit drawing. Evidence is in
`artifacts/v1-native-selection-evidence/three-tables.json`. The raw report retains
an initial diagnostic-only GridView ReloadData overload error; that harness call
was corrected before the successful creation-table checks.

Release/MacOS and Release/Windows configuration builds: zero warnings/errors.
Core suite: 350 passed. `git diff --check` passed. Native Windows and light-theme
checks remain pending. Assembled/installed/companion hashes match; UI SHA-256:
`049cfcc38ef49d58d2630ea73bcdedf114d80986a9e6aea78d8a04ce36f410e7`.
See `artifacts/v1-native-selection-evidence/installed-hashes.json`. Rhino was fully
restarted after installation for these checks. Future bundle updates likewise
require a full quit/reopen. Package results below refer to the preceding candidate;
no replacement Yak was built for this local UI follow-up.

## Changes and evidence

- Reproduced the cached-detail failure in native Rhino: committing a display mode
  succeeded, then the cached wrapper rejected a camera commit and left the old
  camera. Reacquiring the object by ID made the same commit succeed.
- Batch editing and detail rollback now resolve current native objects. A named
  view change preserves the effective sheet mode unless the detail explicitly
  overrides it. Recovery attempts remaining steps and reports failures instead
  of unconditionally claiming success.
- Native regression script: `scripts/rhino-batch-edit-checks.py`. Four checks passed:
  active/inactive combined edits and active/inactive rollback after a later detail
  fails. The fixture requires a genuinely different named-view camera and checks
  camera location/direction, display mode, and assignment metadata. Rollback checks
  both details and serialized metadata. The first harness iteration expected an
  uncommitted ViewInfo camera; the final fixture creates a real native named view.
- Mac tree drag initiation no longer rejects AppKit's drag callback with a second
  distance threshold. Source keys are captured before native selection changes;
  mouse-up/end clear drag state. Only folder/layout name cells start row moves.
  Property cells remain editors, and dragging a detail cannot move its parent.
- Two actual candidate-table drags passed in Rhino: a single layout moved before
  its sibling, then two selected layouts moved together. UI reported one/two
  reordered layouts and retained the respective selections.
- `FoundryTable` owns common typography, row height, selection, striping, and native
  empty-space treatment for the hierarchy, appearance table, and review grid.
  Hierarchy and flat models keep their native tree/grid implementations. Candidate
  hierarchy and Edit layouts review were visually inspected in dark mode; keyboard
  range selection and Escape closing the review dialog worked.

The Mac drag rationale was checked against Eto's [Mac outline handler](https://github.com/picoe/Eto/blob/2.7.3/src/Eto.Mac/Forms/Controls/TreeGridViewHandler.cs),
which calls MouseMove while preparing the native drag pasteboard.

## Validation and delivery

- Release/MacOS solution build: zero warnings/errors.
- Release/Windows configuration also compiled on this Mac with zero warnings/errors;
  this does not establish native Windows behavior.
- Core/Extensibility tests: 350 passed, none failed/skipped.
- `git diff --check`: passed.
- Mac package: `artifacts/v1-edit-fixes-macos/rhino-layout-foundry-0.1.0-rh8_34-mac.yak`.
  All 13 entries, manifest, assembly versions/platform markers and payload hashes
  validated. Yak retains its assembly/package-name advisory.
- Assembled, staged and installed binaries match by SHA-256; the companion's shared
  assemblies match too. Evidence: `artifacts/v1-edit-fixes-evidence/`.
- Native checks used isolated assemblies in Rhino 8.34.26223.11002 and a fresh
  unsaved model. The installed bundle still needs a full quit/reopen before normal
  use. The open Untitled document is the disposable diagnostic model.

Package SHA-256: `5acd99202ed27ddaf61504ea857fbc689f0f1b4e785014a61d9c1973a9ab9c61`.

## Remaining checks

Repeat physical trackpad short/slow drags, folder insertion, drag cancellation,
light-theme/high-DPI/accessibility checks, and native Windows workflows on the
restarted installed bundle. The wider release matrix in TESTING_AND_RELEASE.md
remains required. These focused checks do not certify that matrix.
