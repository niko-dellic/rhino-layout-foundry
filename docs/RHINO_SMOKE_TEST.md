# Native smoke checks for the current candidate

Use fresh schema-16 fixtures and package-6 archives. Record the platform, Rhino version, candidate hash, and pass/fail evidence. Historical fixture compatibility is outside acceptance. The release gate is TESTING_AND_RELEASE.md.

## Creation, templates, and title blocks

1. Create a fresh model containing model geometry, named views, two layouts with different page sizes, details, annotations, and an ordinary page-space block.
2. Open LayoutFoundry. Register a sheet and a detail through the shared template checkbox. Confirm folders have no template role. Change the source page/detail and reopen creation; confirm its template follows the edit. Delete a source and confirm it disappears from choices.
3. Create Blank, Single Detail, both Two Detail arrangements, Four Detail Grid, and a live source template. Set quantity, destination, naming pattern, paper, per-detail named views/display modes/appearance states. Confirm the review table and resulting layouts agree.
4. Test None, Right, and Bottom title blocks. Update project fields, per-sheet numbers and revisions. Confirm only Foundry-generated blocks are managed. Ordinary block instances must remain ordinary page objects.
5. Test equivalent metric/imperial paper and scales, named-view count validation, unavailable sources, and stale drafts. Confirm cancellation/failed creation cleans temporary resources and respects the declared Undo policy.
6. Save, close, reopen, and Save As. Confirm current hierarchy, registration, appearance, naming bindings, project data, revisions, and Canvas placements survive.

## UI and gestures

1. Switch List/Thumbnail/Canvas within the same panel; retain selection and navigation. Exercise folder expansion, filters, sorting, print inclusion, notes, copy/paste, duplicate/delete, and context menus.
2. Scroll the expanded creation accordion in both directions. Resize it, collapse/reopen sections, and verify scrolling alone does not continuously resize content. Cancel and close during queued/in-flight previews; temporary pages must be gone after cleanup completes.
3. Check Tab focus, Enter/Space, Escape, arrow keys, disabled states, dark/light themes, Retina and Windows 100/150/200% scaling. The template checkbox must retain shared-control focus and keyboard behavior.
4. In Canvas, pan below/beside the tree, scroll overflowing tree rows, pinch zoom, and repeat before/after focus changes. A short/empty tree must not steal gestures from surrounding canvas. Ordinary mouse wheel still zooms over canvas.
5. Check Thumbnail density at narrow/full widths, mixed paper orientations, progressive image loading, selection, and right-click actions. Ensure captures retain annotations, shading and native display modes.

## Packages, safety, and scale

1. Export current metadata and ordinary page geometry to .rlf. Import into an empty document and Merge/Replace a populated document. Verify block dependencies, named resources, detail cameras/scales/locks, annotations, appearance assignments and template registrations.
2. Run scripts/rhino-boundary-checks.py on a disposable named fixture. It verifies partial preview ownership, cleanup failure, unsupported-envelope pass-through, import failure at each stage, cancellation, and Replace recovery. Headless checks run last because native Mac ActiveDoc can be cleared by their lifecycle.
3. Run scripts/rhino-preview-edit-check.py with its fresh control/before/after fixtures. Verify the native save prompt and save/reopen; Modified alone is insufficient evidence on Mac.
4. Run scripts/rhino-canvas-scroll-check.py for synthetic routing boundaries, then perform the physical gestures above.
5. Verify ordered PDF output and physical page sizes, cleanup after failure/cancellation, and each operation's declared Undo/Redo behavior.
6. Run the documented 200-sheet/1,000-detail fixture; record cold/warm load, input latency, capture concurrency, and retained memory after switching/closing documents.
7. Install the exact Yak into clean Windows/Mac profiles. Fully restart Rhino between bundle changes and compare installed hashes with staged binaries.
