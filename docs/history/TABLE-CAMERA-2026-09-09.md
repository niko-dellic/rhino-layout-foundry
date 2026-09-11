# Table and camera follow-up — preview.30

> Historical development snapshot. Status, candidate versions and local evidence paths below reflect this dated session; they are not current release acceptance. See the [current clean-break status](../PRE_BETA_CLEAN_BREAK.md) and [testing and release guide](../TESTING_AND_RELEASE.md).

Installed matching `0.3.0-preview.30` shared UI packages into Layout Foundry and Foundry AI, then fully restarted Rhino 8 for macOS.

## Changes

- Markdown table columns fit the actual grid bounds, with room for native gutters/column spacing; minimum column widths no longer force overflow.
- Drawing-plan review uses a native tree table with shared Foundry table presentation. Sheet titles and dimensions, plus detail titles and scales, edit the staged JSON directly. Detail rows can collapse under their sheet. Destination selection remains separate. No document changes occur while editing.
- Agent camera display conduit includes its full indicator geometry in scene clipping bounds. This does not add a Rhino object or change the agent's capture capabilities.

## Verification

- Both isolated host builds: zero warnings/errors.
- Tests: 448 public Core, 57 AI Core, 4 shared UI, 15 shared primitives; all 524 pass.
- All three repositories: `git diff --check` passes.
- Installed DLL/RHP bytes match isolated candidate outputs; SHA-256 hashes checked. Install scripts also verified the shared package manifest.
- Native dark-theme Retina fixture: Markdown table and seven-sheet/16-row plan table at 820px and 520px widths, without horizontal scrollbars.
- Pointer cell edits changed a sheet width to 450 and a detail scale to 50; serialized staged JSON contains both values. Collapsing a sheet hides its detail rows.
- Temporary camera indicator outside model bounds remained complete from three test viewing angles. Original viewport projection restored and temporary conduit disposed afterward.
- No paid API calls and no sheets or geometry created during verification.

Not certified by this check: Windows, light theme, full approval/model execution, very large plans, or every possible viewport clipping configuration. The native fixture scripts/results are in `/private/tmp/foundry-table30-check.py`, `/private/tmp/foundry-table30-result.json`, and `/private/tmp/foundry-camera30-check.py` for this development session.
