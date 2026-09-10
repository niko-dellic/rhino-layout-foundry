# Readiness fixture: V1 end-to-end exercise

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
- Conservative cumulative API ledger: $4.575264 of $10, 12 generation requests; not a provider billing statement.

## Remaining release gate

This is a successful creation fixture with local output verification, not an unattended production-readiness certification. A further external AI review of the corrected floor-plan and section images was blocked by the permission reviewer; no retry or workaround was used. Fresh explicit authorization for transmitting those images to the configured OpenAI provider is required before that recheck. The final linework default still needs a fresh uninterrupted creation regression run. Recovery intentionally expires mutation approvals and excludes image payloads.
