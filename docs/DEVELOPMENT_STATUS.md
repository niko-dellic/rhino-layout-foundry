# Development status

Foundry is preparing for public v1 on Windows and macOS. Package version remains defined in Version.props; no public release is implied by a development installation.

## Implemented hardening

- Protected metadata states with original-envelope preservation, write guards and a persistent UI diagnostic.
- Preview resource ownership, complete cleanup attempts, and removal of unsafe deferred modified-flag resets.
- Import dependency journaling and recovery packages for Merge and Replace.
- Smaller creation controls and draft/session models, a shared preview scheduler, separated mutation dispatcher/families, and instance-owned application workflows.
- Independently tested experimental automation approval registry with defensive plan copies.
- Explicit platform builds, pinned SDK/language policy, lock files, shared versioning, and candidate staging verification.

## Evidence and outstanding gates

Local automated verification and packaging results are recorded in [V1 validation](V1_VALIDATION.md). The [Mac preview modified-flag discrepancy](PREVIEW_SAFETY_VALIDATION.md) was resolved on September 4 through a corrected control/before/after test and native save/reopen checks. Licensed Rhino acceptance remains separate from unit test results. Public release requires every applicable Windows/Mac check in [Testing and release](TESTING_AND_RELEASE.md); the workflow does not publish automatically.

Known deliberate limitations: non-undoable native operations remain disclosed; canceled live previews can leave the document marked modified; failed replacement recovery can recreate native IDs; the companion API remains experimental.

Historical milestone narrative is retained in history/DEVELOPMENT_STATUS_PRE_V1.md. It is not evidence that a current candidate passes a host check.
