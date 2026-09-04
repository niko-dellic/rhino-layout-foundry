# Changelog

## Unreleased — v1 hardening

- Protect unsupported or invalid Foundry metadata from editing and preserve readable archive envelopes when saving.
- Register temporary preview pages before construction continues; attempt all cleanup and always restore Undo recording.
- Remove deferred modified-flag resets. Canceling a live preview may leave an unsaved-change indicator to protect real edits.
- Journal import dependencies and overwritten named resources; preserve a recovery package and report incomplete rollback.
- Separate mutation dispatch, operation families, application workflows, creation controls, draft state, and preview scheduling.
- Stop repeated settings-pane layout and repainting when its viewport width has not changed, improving creation/edit dialog scrolling on macOS.
- Limit canvas tree gesture interception to visible rows; allow panning below short trees and prevent tree scrolling from falling through to canvas zoom.
- Freeze staged automation plans and test expiry, revisions, and single-use approvals. The companion API remains experimental.
- Centralize versions and platform selection, lock dependencies, validate candidate bundles, and distinguish portable CI from native host verification.

No public v1 release has been approved by these changes. See docs/TESTING_AND_RELEASE.md for the remaining release gates.
