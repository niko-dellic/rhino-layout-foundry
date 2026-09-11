# Coordinated pre-beta clean break

This working candidate requires Layout Foundry and Foundry AI together. It has not been published, signed or qualified for beta. Previous binary/fixture evidence does not certify this change.

## Supported formats and workflow

- Automation protocol 2; drawing-set specification 2 with explicit `hidden_layer_ids` on every view.
- Document schema 17 and package format 6 are unchanged. Unsupported or malformed Foundry metadata is preserved and blocked from editing; native Rhino geometry is never reset or deleted as a migration.
- Conversation checkpoint 2 contains provider history plus ordered typed presentation events. Version 1 conversations do not migrate. The current Index/Record/Latest store remains; unsupported records are diagnosed and protected against overwrite.
- Open Drawing Set inside Layout Foundry, submit a brief, answer questions, review and explicitly approve a plan, inspect the results, then save the Rhino model. Restoring history grants no approval and runs no action.
- Basic manual sheet creation, corrections, active hierarchy duplication and linked captions remain supported.
- Release excludes the AI demo driver. Debug fixtures and fault injection remain available for regression checks.

## Demonstrated validation — September 11, 2026

| Check | Result |
| --- | --- |
| macOS Debug and Release builds, both repositories | Zero warnings and errors |
| Windows Release builds, both repositories, cross-built on macOS | Zero warnings and errors |
| Core suites in each configuration | 449 Layout + 85 AI = 534 passed; zero skipped |
| Shared dependencies and installed macOS Debug hashes | Match staged assemblies |
| Release demo driver | Absent; retained in Debug |
| Local Yak packages and matched private archives | Verified for both platforms; no upload |
| Both working trees | `git diff --check` passed; prior changes preserved |

Native verification used **Rhino 8.35.26251.13002 on macOS**, Dark appearance, and the final matched Debug pair. The fresh final fixture is `artifacts/pre-beta-clean-break-20260911/native-final/foundry-boundary-fixture.3dm`.

Demonstrated native checks:

- Basic creation and live registrations, including imperial conversion and caption registration.
- In-panel brief → structured question → visible plan review → explicit approval click → one scaled/clipped sheet → capture and visual review. The provider was scripted and offline: six responses, no external requests. This verifies the native workflow, not live AI quality.
- Nine preview/metadata checks, including current save/reopen/Save As and protected preservation of schema-16, malformed and mismatched envelopes.
- Unsupported checkpoint record retained beside valid records, refused on overwrite, preserved in the current index after a valid write.
- Active hierarchy duplication retained native detail-caption ownership.
- Full Rhino quit/restart and checkpoint-v2 reopen: 25 ordered events, manual title, six sheets, resource links and attachment placeholders. Completed questions stayed closed; no approval or image bytes were restored and no geometry was created by restore.
- Restored resource Open navigated from another page to the correct generated sheet.
- Both protocol mismatch directions refused a task with update instructions; the removed creation operation was rejected; loaded assembly hashes matched the final staged pair.

Earlier exploratory runs are under `native/`. Only `native-final/` is used for final native evidence. Reports, build/test logs, assembly and installed hashes, archive hashes and an evidence manifest are under `artifacts/pre-beta-clean-break-20260911/`.

## Remaining release gates

- Native Windows installation, UI, document behavior and uninstall remain untested. Cross-building and running Core tests on macOS is not Windows-native validation.
- Live-provider acceptance with a representative project remains untested.
- The broader beta UI, Undo, performance, PDF cancellation and isolated-profile installation matrix remains open for this candidate. Historical performance or UI passes do not carry forward.
- Native Release-package installation still needs qualification; the macOS workflow above exercised the matched Debug pair.

## Private handoff

`matched-Windows.zip` and `matched-MacOS.zip` contain matching Release binaries and manual installation notes; `ARCHIVE-SHA256.txt` identifies them. The local Yak packages are Layout-only verification artifacts, not the complete AI pair. Existing version labels are development labels; do not use these as an in-place public Yak update.

Windows manual installation requires no SDK or source checkout. Extract all compiled files together and install both RHP files through Rhino Options → Plug-ins → Install. A polished consumer installer is not included. The macOS archive is a staged binary set; the development installers assemble the bundle directories.

Rhino must be fully closed when replacing either plugin. Restart it afterward. The final matched Debug pair is installed locally, its hashes verified, and Rhino has been fully restarted. No publication, signing, release tag or commit was performed. Rhino Package Manager publication requires explicit user instruction.
