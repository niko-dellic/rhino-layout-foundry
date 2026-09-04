# Edit and table fixes — 2026-09-04

This development bundle follows the pre-release cleanup candidate. It is not a
published release or a completed Windows/macOS release sign-off.

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
