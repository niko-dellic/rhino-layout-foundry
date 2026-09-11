# Readiness fixture: V1 end-to-end exercise

> Historical development snapshot. Status, candidate versions and local evidence paths below reflect this dated session; they are not current release acceptance. See the [current clean-break status](../PRE_BETA_CLEAN_BREAK.md) and [testing and release guide](../TESTING_AND_RELEASE.md).

## Conversation polish — preview.33

- Installed coordinated UI preview.33 and matching Layout/AI bundles, with zero build warnings/errors and matching installed RHP/UI/Core hashes. 532 automated tests and 8 existing native HostChecks passed.
- Conversation tables now use content-height cells with optional per-column wrapping; Markdown enables wrapping. Native main hierarchy grids remain unchanged. Verified wrapped text and outer-document scrolling over table cells in a native fixture and restored conversation.
- Long creation activities collapse behind a short summary. Created-resource events aggregate into a collapsible Type/Name/Open table, rather than one full-width button per resource.
- Composer has an internal lower-left + menu for files/folders. Queue supports PNG/JPEG and UTF-8 TXT/MD/CSV/JSON/YAML, eight-file limit, bounded file/text sizes, remove-before-send controls. Folder enumeration is top-level only; hidden files/symlinks and unsupported files are reported rather than silently uploaded. No automatic send.
- Native attachment menu placement verified. File/folder picker and removal checks remain outstanding because computer control reported concurrent user interaction twice; automation stopped rather than competing with the user. New resource table actions, light theme and Windows also need native sign-off. No paid generation requests or document mutations in this polish pass.

## Fresh 30-sheet stress test — September 10

- Follow-up completed after explicit approval to raise the cumulative cap to $20: resumed the existing conversation without creating another set, captured/reviewed all 30 sheets, and saved the final review in readiness.3dm. Final conservative ledger: $15.055596, 28 generation requests (not a provider invoice).
- AI review passed populated-sheet count, readable titles/captions, caption/settings scale agreement, whole-building framing on 01–18 and visible intended clipping/opposite stair-section directions. Remaining observations: outlying source-model object; surrounding context/white space in enlarged stair studies; tight crop on 25; missing continuation/locator references; limited cut/background line hierarchy. No geometry was removed to conceal those findings. Exact clipping-plane origins/assignments were not independently auditable through the scoped post-creation inventory. This completes the resumed model-study review, not an uninterrupted run or a construction-document certification.
- Installed shared UI preview.32: rich question titles/descriptions, recommendation badge, pencil custom entry, explicit Skip and asynchronous cancellation; compact muted activity rows. Offline Skip/final-choice submission and X cancellation passed. The live run also answered a rich question without a crash. A font-ownership crash found in preview.31 was fixed before this run. Dark macOS/Retina verified; light theme and Windows remain unverified.
- Created `V1 30-Sheet Stress Test` through the Rhino UI; existing-sheet review was explicitly disabled. Initial gallery contained only one model inspection image.
- Fresh conversation measured geometry, clarified the deliberate study variants, staged all 30 sheets, and created 30 scaled details with one approval and no restart or crash. The model corrected a roof-plan cut validation error before approval.
- Scope: 6 plans, 4 elevations, 8 building sections and 12 enlarged stair studies; A3 landscape, one parallel detail each. These are model studies, not fabricated construction details.
- All 30 local captures succeeded and were visually inspected. Titles/captions and black linework are readable; whole-building views are framed and stair studies use intentional crops. Existing outlying geometry remains visible where applicable and was not removed.
- Local evidence: `/private/tmp/stress-30-qa.json`, `/private/tmp/stress-sheet-01.png` through `stress-sheet-30.png`. Saved `/private/tmp/foundry-readiness-dPDXbK/readiness.3dm` after creation.
- Latest validation: 448 Layout + 65 AI + 19 shared tests passed; installed/assembled RHP, UI and Core hashes matched; whitespace checks passed.
- Cumulative conservative API ledger is $9.456744 of the original $10 cap, 23 requests. This fresh run added $3.110100. The next request was blocked by the budget guard after creation/document inspection, so the in-product AI visual-review loop is **not complete**. No further paid calls were made. This is not a full unattended V1 certification.

## Result

Created a new `V1 Readiness Test` folder in readiness.3dm and generated four A3 landscape sheets, nine parallel 1:100 details, in one approved batch. Existing sheets were excluded from the initial agent inventory and capture scope. The model measured levels and stair geometry itself. No cover/placeholder sheets were requested or generated.

Sheets: Floor plans (2 details), Roof plan (1), Exterior elevations (4), Building sections (2).

Native clipping verification: floor cuts Z=1200 and 4200, normals -Z; stair cut X=7045 normal -X; transverse cut Y=4375 normal +Y. Each new clipping plane has exactly one intended detail viewport assignment.

## Defects found and handled

- Vision budget reservation incorrectly treated base64 image bytes as text tokens. Budgeted requests now use the provider input-token count endpoint before generation and fail closed on count failures. Cumulative accounting survived restarts.
- Recovery replay reopened already-answered clarification questions. Only the final conversational message may activate the recovered question composer.
- Native Pen mode inherited pale section/object colors. Generated drawing sets now use a dedicated `Foundry Drawing v1` mode with solid black clipping edges and section-style inheritance disabled. The built-in Pen mode and original sheet display modes are not changed. All nine new fixture details were corrected and locally recaptured.

## Evidence / validation

- Actual UI brief, question response, plan approval, creation, capture and restored-conversation flow exercised in Rhino 8 on macOS.
- Initial agent visual QA checked four pages and nine details; framing, scale and captions passed. It identified pale cut lines and an outlying model object.
- After correction, local 1600px captures confirmed readable cuts, complete framing and labels on all four pages. Outlying model geometry was left untouched.
- Both affected host builds: zero warnings, zero errors.
- Core tests: 448 public + 62 AI passed. Installed RHP/UI/Core SHA-256 hashes matched assembled artifacts. Diff whitespace checks passed.
- Saved model: `/private/tmp/foundry-readiness-dPDXbK/readiness.3dm`.
- Local captures: `/private/tmp/v1-sheet-1.png` through `v1-sheet-4.png`.
- Final externally reviewed captures, explicitly authorized by the user on September 10: pale cut-line defect resolved; both floor plans and both sections have complete framing, readable captions and exact 1:100 parallel details. No creation or edits during this review. Existing outlying rectangle and relatively uniform line weights remain observations, not framing failures.
- Conservative cumulative API ledger: $6.346644 of $10, 15 generation requests; not a provider billing statement. Final conversation saved in the model.

## Remaining release gate

This is a successful creation fixture with local and external visual verification, not an unattended production-readiness certification. The external recheck was initially blocked; after fresh explicit user authorization it completed successfully through the Rhino UI. The final linework default still needs a fresh uninterrupted creation regression run. Recovery intentionally expires mutation approvals and excludes image payloads.
