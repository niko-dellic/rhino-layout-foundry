# Settings scrolling investigation — 2026-09-04

Host: Rhino 8.34.26223.11002, macOS 26.6.2 arm64, dark theme at Retina scale.

The creation/edit settings pane resynchronized its accordion's MinimumSize and Width on every Scrollable.SizeChanged notification, then explicitly invalidated the content. On this AppKit host, those assignments caused another size notification even when the viewport width had not changed. The dialog continually laid out and repainted its controls while idle.

The correction remembers the last applied viewport width before assigning either property, skips repeated widths, and removes the explicit invalidation. Genuine width changes retain the existing manual width synchronization needed for fields to shrink again after widening.

## Observations

- A temporary probe attached SizeChanged/Paint counters to the installed edit dialog. Between probe elapsed times 100.4 and 167.5 seconds, the accordion and scrollable each received another 2,109 size notifications (about 31 per second). Final totals were 3,896 size notifications and 42,856 field-chrome paints. The counters were already increasing before scrolling.
- The corrected UI assembly was built separately and loaded from bytes into an isolated diagnostic dialog, using a read-only snapshot of the same Page 1. Its application service had no host providers; Apply was disabled. This allowed testing without replacing the loaded plug-in or restarting the unsaved document.
- In the corrected dialog, accordion and total scrollable size counts stayed at seven between 8.5 and 28.2 seconds, including scrolling down and back up. Field-chrome paints increased from 22 to 33. Opening, collapse/reopen, and widening/narrowing the pane produced the expected visible layout changes without restarting the continuous cycle.
- Escape closed the diagnostic dialog without applying document changes. The original document remained open with its existing unsaved-change indicator.
- UI and host builds: zero warnings/errors. Existing core suite: 353 passed. `git diff --check` passed.

Local probe scripts and JSON evidence are retained under `artifacts/scroll-safety-2026-09-04/` (ignored development artifacts).

These counts demonstrate that the repeated layout cycle stopped; they are not frame-rate measurements. The original probe attached after opening, while the candidate probe included initial layout, so their initial counts are not directly comparable. The candidate probe intentionally omitted native preview capture. Full integrated creation/edit workflows after restart, Windows behavior, light theme, and the broader release matrix still need their normal sign-off.
