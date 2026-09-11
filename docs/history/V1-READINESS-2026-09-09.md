# Readiness pass — 2026-09-09

> Historical development snapshot. Status, candidate versions and local evidence paths below reflect this dated session; they are not current release acceptance. See the [current clean-break status](../PRE_BETA_CLEAN_BREAK.md) and [testing and release guide](../TESTING_AND_RELEASE.md).

This is an interim acceptance report, not a v1 sign-off.

## Implemented

- Bounded model-capture reuse with document identity, camera/frustum, viewport size, display settings, geometry, attributes, layer and material state. Reused results are identified to the AI activity layer without another image card.
- Shared multi-select closes suggestions after Enter and when focus leaves the search/results controls.
- Document-identity invalidations bypass the normal other-document event filter, so switching documents can refresh the main table.
- Offline HTML/CSS comparison prototype in the sibling shared UI repository: `tools/conversation-renderer-prototype.html`. It is not shipped as the conversation renderer.

## Observed in Rhino 8.35, macOS, dark theme

- Two consecutive native model captures: first fresh, second reused; both succeeded (63,941 bytes).
- Changing the camera caused a fresh successful capture.
- Adding temporary geometry caused a fresh successful capture. Test geometry was removed and the camera restored.
- The real roof-plan form was filled in a separate copy at `/private/tmp/foundry-readiness-dPDXbK/readiness.3dm`; existing geometry/sheets were not replaced.
- Found lingering drawing suggestions and a stale document name in the main table after switching. Both received source fixes; final native retest is tracked separately below.
- Offline HTML prototype displayed wrapping tables and semantic question controls through Rhino WebView. Explicit UTF-8 loading fixed mojibake in the test harness. Disclosure interaction, virtualization, image-memory behaviour and Windows remain unverified. Do not adopt yet.

## Blocked / not passed

- Safety review rejected live submission of document inspection/captures to OpenAI. An explicit approval question was sent for the exact payload and $3 cap. No request was dispatched and no API spend incurred in this pass.
- Therefore proposal, clarification, edited approval, creation, refinement, cancellation during a live request and provider-failure recovery were not verified end-to-end.
- The Submit action was difficult to reach in the narrow pane; keyboard traversal did not reliably reach it. This remains a release blocker, not a passed check.
- Final restart retest: switching to an already-open tab still left the previous document name visible in the layouts table. The invalidation-filter correction alone is insufficient. Document switching remains a failed acceptance check; do not infer wrong-document-write safety from the table.
- The popup source fix builds and is installed, but a full keyboard/mouse/collapse native retest remains outstanding.
- General render effects/conduits and all cache invalidation edge cases are not certified by the camera/geometry smoke tests.
- Windows/light-theme/high-DPI matrix and long-history performance remain outstanding.

## Build verification

Coordinated shared package preview.21; isolated public/private builds have zero warnings/errors. AI (50), public core (445), shared UI (4), and primitives (15) tests passed. Whitespace checks passed. Installation compares exact shared package binaries; host output comparisons are byte-for-byte.
# Paid readiness pass: checkpoint invalidation fix

User authorized a cumulative $10 maximum for this pass. The initial $3 guard
blocked dispatch; the same captured request was retried with a $10 guard.
GPT-6 Astra completed eight requests (228,686 input / 1,022 output tokens),
settling at $2.805552 under the repository's conservative pricing estimate.
No further requests were sent after this settlement. A future continuation
must resume this usage, not reset the $10 allowance.

The model proposed one A3 monochrome roof plan at 1:100 while preserving all
existing sheets. Staging failed twice because conversation autosaves advanced
the document revision. A local before/after checkpoint probe confirmed 116 → 119.

Implemented a document-bound, thread-local conversation metadata write scope.
Only document-property notifications within that synchronous scope skip drawing
revision increments; geometry, layer, view and command events remain tracked.
Installed both rebuilt bundles and fully restarted Rhino. Native regression:
checkpoint metadata 30 → 30; test geometry advanced revision to 31. Test point
removed. Saved conversation reopened; eight existing sheets / 18 details remain.

Validation: public 447 tests and AI 50 tests passed; both builds zero warnings
and errors; both repository diff checks clean. Installed RHP hashes and public
Core/UI/Extensibility binaries match isolated outputs.

Remaining: rerun actual plan approval/creation after this fix, investigate extra
captures, finish Submit reachability and document-switch checks, and complete
renderer/accessibility/performance acceptance. No v1 sign-off. Computer screenshots
and coordinate clicks intermittently returned noWindowsAvailable; native keyboard
and accessibility inspection remained usable. Some workflow controls were invoked
through their real handlers using isolated Rhino Python test helpers; this is not
a pass for ordinary-user mouse/keyboard reachability.

Evidence is under `/private/tmp/foundry-readiness-dPDXbK`: `session.json`,
`usage.json`, `checkpoint-revisions.txt`, `checkpoint-fix-result.txt`, and saved
`readiness.3dm`. The JSON is local test evidence and must not be published.
