# Linked detail captions

New basic and AI-created details are explicitly tagged for caption management.
Old, untagged details remain unchanged. Right-click one folder, sheet, or detail
and choose **Detail captions…** to set Name and Scale independently to Inherit,
Show, or Hide. Detail overrides beat sheet overrides, which beat the nearest
folder override. Both default to Show; perspective always suppresses scale.

Text follows the detail's paper-space bottom-left corner at a 2 mm gap and 2.5 mm
height. The name follows the detail's current object name. Scale uses Rhino's
native `%<DetailScale("object-guid","1:#")>%` field. Both hidden preserves ownership.
Maintenance runs on idle, after commands, never inside draw callbacks or Undo/Redo.
The idle fallback checks every 500 ms to pick up viewport property changes.
Only changed geometry/visibility is written. Derived maintenance does not add undo
records; caption preference operations use Foundry's existing custom undo state.

Preferences use `caption.<scope kind>.<scope id>.<name|scale>` in document metadata.
No document-format migration is required. Package details have an optional
`HasManagedCaption` flag (false for old packages); imported managed captions are
regenerated against destination IDs. Foundry hierarchy duplication copies overrides
to destination folder/page/detail IDs, retaining inheritance rather than flattening it.

Automation exposes `stage_set_detail_captions` with required `scope_kind`
(Folder/Sheet/Detail), `scope_id`, `name`, and `scale` (Inherit/Show/Hide).
It stages a revision-checked operation, never bypassing approval. Inspection reports
overrides, effective detail visibility, managed status, and caption overflow warnings.
The tool uses the existing strict function schema; see OpenAI's official
[function-calling documentation](https://developers.openai.com/api/docs/guides/function-calling).

## Native acceptance checks (pending installation)

- Create a basic two-detail sheet and an AI sheet. Confirm one caption per detail,
  correct ratio, physical text height, and no changes to pre-existing details.
- Change scale in Rhino properties, rename, switch parallel/perspective/back,
  move and resize the frame. Verify updates without a second caption.
- Exercise Name/Scale independently at folder, nested folder, sheet and detail;
  move sheets between folders and confirm inheritance changes.
- Hide both, save/reopen, then show both. Delete detail, undo, redo; check caption
  count and that maintenance does not interrupt the redo sequence.
- Duplicate twice, export/import into another document, then change the copied
  detail's scale; verify its field references the copy, not the source.
- Check narrow frames/page-edge warnings without automatic geometry changes.
- Print/PDF parallel and perspective details; no `####`, stale ratios, or unwanted
  scale text. Check editor keyboard operation in light/dark at Retina scale.

Unit tests cover independent inheritance, perspective suppression, field composition,
ID remapping, metadata round-trip, untagged defaults, stale revisions, and missing targets.
