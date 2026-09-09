# Review scope and input layout — preview.26

Installed both coordinated bundles and restarted Rhino. Installed DLL/RHP SHA-256
hashes match isolated candidates. Builds: zero warnings/errors. Tests: 447 public,
55 AI, 4 shared UI, 15 primitives (521 total). No paid provider requests.

- Growing custom fields suppress the native macOS scroll border. Native offline
  question fixture shows a single shell border.
- Composer height reserves its growing editor, action row and padding. Native
  offline empty-composer fixture shows its action inside the bottom-right edge.
- Clicking an inclusion cell affects only that row; toolbar and Space retain
  bulk selection semantics. Mouse-up suppression no longer races an async reset.
- Restricted model-facing inventories contain only eligible sheets, and omit
  document-wide selection, provenance, receipt, template, named-view and clipping
  indexes. Full internal snapshots remain available to authorization tracking.
  Existing named-view capture is blocked in restricted scope, except views owned
  by the current run. Selected-sheet capture enforcement remains in place.
- Legacy restricted-scope checkpoint resume is rejected with guidance to start
  a new conversation; the saved conversation is not deleted.

Scope regression tests verify excluded layouts and secondary indexes are absent,
including when review is disabled. This is not a guarantee that selected-sheet
notes/images or model geometry cannot themselves mention other drawings. It is
not a paid end-to-end provider test or Windows/light-theme sign-off.
