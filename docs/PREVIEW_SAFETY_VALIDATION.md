# Preview safety investigation — 2026-09-04

**The Mac modified-flag test blocker is resolved.** The previous test used a platform-inappropriate assertion. No production change was required during this investigation; the existing preview sessions still never reset `Modified`.

## Cause and evidence

The previous harness manufactured a clean/dirty baseline through `RhinoDoc.Modified`, then treated its value as the sole authority for native unsaved changes. McNeel explains that macOS manages document changes separately and setting this property does not control that state. See [McNeel's explanation](https://discourse.mcneel.com/t/still-prompted-to-save-after-doc-modified-flag-set-to-false/179427/4). The generic API reference does not describe that platform limitation.

On this host, a **no-preview control** returned `false` immediately after adding a real point and setting `Modified = true`. Rhino nevertheless displayed Edited and prompted to save. Its API value later became true after idle. This reproduces the invalid dirty-baseline assumption independently of preview cleanup. In both preview cases, the API remained false after idle while native save protection still worked.

Verified on macOS 26.6.2 arm64, Rhino 8.34.26223.11002, using disposable copies of the same four-sheet/ten-detail source. The installed assembly hashes still match the candidate recorded in [V1 validation](V1_VALIDATION.md).

| Case | API Modified immediately / after idle | Native Edited and Save prompt | Point preserved after native save/reopen | Page count and Undo recording restored |
| --- | --- | --- | --- | --- |
| Normal edit, no preview | false / true | Passed | Passed | Passed |
| Real edit before preview cleanup | false / false | Passed | Passed | Passed |
| Real edit immediately after preview cleanup | false / false | Passed | Passed | Passed |

The save prompts were observed through Rhino's native UI for each named test document. Save was selected, the document was reopened, and the exact generated point ID was found. After reopening, each test document closed without another save prompt. The original source document was left open and unchanged.

## Regression correction

`scripts/rhino-preview-edit-check.py` now runs control, before-preview, and after-preview cases. It no longer sets a false Mac dirty baseline. It verifies page ownership, Undo recording, real object survival after idle, and native save/reopen persistence. On Windows it additionally asserts the modified flag after idle. On Mac the flag remains a diagnostic value; native Edited/save-prompt observation is an explicit independent sign-off. The script never invents that sign-off from its API result.

The three script reports are retained locally in `artifacts/preview-safety-2026-09-04`. Their passed status covers the script assertions; this document records the separately observed native save prompts. Use the [test procedure](TESTING_AND_RELEASE.md#native-regression-scripts) to repeat the checks in a fresh Rhino session.

Final repository checks: UI/dependency build passed with zero warnings/errors; all 353 core tests passed; `git diff --check` passed; assembled and installed binary hashes still match the existing candidate. No binary installation or Rhino restart was needed for these test/documentation changes.

The prior failed report is historical evidence of the incorrect test, not an outstanding Mac defect. This closes only the investigated modified-flag discrepancy. Windows and the remaining release matrix, including provider-level construction faults and other lifecycle scenarios, still require sign-off.
