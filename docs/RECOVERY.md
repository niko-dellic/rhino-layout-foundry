# Recovering documents and imports

Work on a copy of an affected 3DM. Keep the original file and any `LayoutFoundry-Recovery-*.rlf` package until the result is verified.

## Unsupported or invalid metadata

Foundry shows a persistent diagnostic and blocks its mutation entry points. Rhino layouts remain available through Rhino. When the archive dictionary is readable, Foundry passes the original dictionary through on save, including unknown versions and fields. Merely inspecting a document does not prune its metadata.

Use a compatible Foundry version to edit newer metadata. Schema 11 was an intentionally unsupported pre-release format. Do not delete metadata to silence a warning. If the underlying archive cannot be read at all, Foundry refuses to write replacement empty metadata; preserve the original 3DM and recover into a separate copy using Rhino's native tools.

Foundry accepts document schema 16 and package format 6 only. Historical formats are unsupported. There are no converters, compatibility aliases, or migration paths. The generic protected-state behavior preserves recoverable unknown metadata without interpreting it. Use fresh documents and packages for this candidate.

## Failed or canceled package import

A recovery package is captured before importing dependencies, for Merge as well as Replace. The failure result reports its path. Keep this file: temporary folders can be cleaned by the operating system.

Foundry attempts to remove created resources, restore overwritten named views and layer states, restore touched layer/object attributes, and restore metadata. After Replace cutover it also attempts to recreate the original layouts from the recovery package. Recreated Rhino object/layout IDs can differ. A report of incomplete recovery requires inspection of layouts, named resources, and metadata; do not assume one Undo will recover the operation.

Copy the recovery package to a durable location. Import it into a separate document and verify physical page sizes, details, title blocks, named resources, and project information before replacing your working copy. Include the diagnostic, Foundry version, Rhino version, platform, and reproducible steps in an issue report; share model files only if appropriate.

## Preview cleanup

Previews temporarily use the active document because Rhino captures depend on its display context. Cleanup attempts every restoration even when a resource fails. Foundry never clears a document's modified flag from a delayed preview callback. A canceled preview can therefore leave an unsaved-change indicator. If cleanup reports an error, inspect for temporary `__FoundryPreview_` or `__FoundryEditPreview_` layouts before saving a copy.
