# AI drawing-set v1: implementation and release gates

Status: engineering alpha. This document is a release plan, not a claim that the
complete customer workflow is implemented.

## Current development slice — targeted verification completed

The user subsequently authorised testing with an additional $10 allowance. Both
hosts build cleanly and are installed; 438 public and 23 private tests pass. Native
v2 creation/save/reopen verified five sheets, nine scaled details, fourteen captions,
four scoped cuts and eight visibility overrides. A forced second-sheet caption
failure rolled back while preserving original object/page/layer/view IDs and receipts.
Actual modal conversation save/resume passed without an API call on load. The one
live Astra request was rejected for unavailable provider credit/quota; its separate
journal retains a conservative $0.703428 reservation, not a confirmed charge.

- The model tool now proposes schema version 2; the parser retains version-1 support.
  Version 2 reserves title/caption space, generates editable page-space titles and
  drawing/scale captions, and places details/captions on a proposal-owned layer.
- Explicit `hidden_layer_ids` lists use inspected IDs and apply visibility overrides
  only to newly created detail viewports. Empty lists preserve existing visibility;
  globally hidden objects are not revealed. Approval summaries name hidden layers.
- Rollback tracks captions, presentation layers and new visibility scopes as well as
  pages, named views and clipping planes. Caption overflow fails instead of silently
  clipping text. Verify native font metrics and cleanup in the final pass.
- The AI modal gains explicit Save conversation / Resume conversation actions.
  User-selected local JSON contains the brief and conversation, excluding configured
  credentials and image parts. It is path-bound, size-limited and atomically replaced;
  Unix files are owner-only. Text can still contain private project information.
  This is explicit recovery, not automatic crash checkpointing or a Rhino model save.
- Resume expires pending approvals, resolves unanswered calls as uncertain and asks
  for fresh document/receipt inspection. It does not automatically send a request or
  recreate sheets. Only trusted conversation files should be opened.

Final verification must cover v1 parsing compatibility; v2 caption spacing/overflow;
layer visibility isolation and persistence; native failure cleanup; save/resume with
pending and interrupted calls; wrong-document/corrupt checkpoint handling; macOS and
Windows file dialogs and privacy; keyboard traversal and dark/light/high-DPI UI.
White-paper capture was implemented using a temporary/restored native paper preference.
Automatic crash checkpoints and native Undo remain open. Fresh AI verification,
light-theme/high-DPI coverage and Windows remain release gates, not passed checks.

## Product boundary

Create editable general-arrangement sheets from an organised Rhino model. Initial
scope is site plans, floor plans, elevations and sections. No PDF export, arbitrary
code execution, submission-compliance claims, construction schedules or automatic
changes to building geometry. Existing drawings remain usable without a subscription.

## Ask users only for decisions they own

| Information | Behaviour |
| --- | --- |
| Drawing types | Required intent; offer selectable suggestions. Never silently add drawings. |
| Page format | Default from document; user can change before approval. |
| Destination folder | Default from selection/root; show it in the proposal. |
| Provider/model/key | User-level setup, not a per-document requirement. Never embed secrets in a plan. |
| Building/site scope | Infer from selection and inventory; ask when there are multiple plausible subjects. |
| Storeys | Propose elevations from geometry/layers; ask for confirmation if ambiguous. |
| Orientation | Infer coordinate axes; do not call them geographic north without evidence. |
| Cuts and framing | Agent proposes; deterministic code validates; user reviews a visual preview. |
| Submission/stage | Optional context, not a barrier to making sheets. |
| Audience/section coordinates/output requirements | Not required form fields. |
| Budget | No mandatory per-document money-cap field. Optional user-level spending settings. |

Do not silently invent a cut, level, camera or geographic orientation because a
required technical property is missing. Technical properties belong in the
agent-authored proposal, not the customer form.

## Cost policy

Token throughput limits, output-token limits and dollar spending limits are distinct.
Do not infer a dollar allowance from a configured API key or request administrative
credentials merely to inspect billing settings. Provider rejection remains authoritative.
Use bounded tool/request loops and cancellation regardless of optional dollar settings.
Expose estimated usage without presenting estimates as invoices. A quota error must
preserve work and offer resume; never repeatedly retry exhausted credit.

Official documentation inspected on 2026-09-07 describes separate project rate limits
and project hard spend limits:

- https://developers.openai.com/api/reference/typescript/resources/admin/subresources/organization/subresources/projects
- https://developers.openai.com/api/reference/ruby/resources/admin/subresources/organization/subresources/projects/subresources/rate_limits/methods/list_rate_limits

The newly authorised $20 is an additional debugging ceiling, not an automatic credit
purchase or customer default. Future live testing must use a separate journal and
preserve the previous demo journal. No new billable calls were made for preflight work.

## Contract foundation implemented

`DrawingSetSpecification` is a versioned proposal with stable logical keys, explicit
document/revision binding, destination, sheet geometry and per-view cameras, scales
and optional cuts. Page-space values use millimetres; model-space values use document
units. Floor plans and sections require explicit cuts. The companion bridge exposes
the read-only `validate_drawing_set` operation, returning field-addressed issues and
sheet/view counts. It neither stages nor applies mutations.

Parsing rejects unknown, duplicate and omitted writable properties. Validation rejects
unsupported versions, stale documents, missing folders, invalid/duplicate names and
keys, invalid scales/cameras/cuts, excessive counts and overlapping/off-page details.
Valid JSON is not approval and is not proof of architectural correctness.

`DrawingSetPlanner` now compiles a valid proposal into one `CreateDrawingSetChange`.
The bridge exposes `stage_drawing_set` through the existing approval registry. Nested
collections are frozen, a SHA-256 digest binds the specification, and normal revision,
expiry and single-use approval checks apply. The approval summary lists sheet sizes,
drawing names, scales and whether each view is clipped.

The native batch executor creates pages, parallel locked details, generated named
views and detail-specific clipping planes. It verifies scale/projection/display mode
and native clipping assignments. A document-metadata receipt records the digest and
created resource IDs, rejecting repeat creation. Exceptions trigger scoped cleanup;
cleanup failures are reported rather than described as successful rollback. The
previous active view is restored. Unitless/custom-unit documents are rejected.

The batch path is now exposed in the private agent tool catalog through a shared
strict JSON schema. A native Rhino smoke test created five A3 sheets, nine locked
details and four viewport-scoped cuts. Saved and reopened resource IDs, scales,
clipping assignments and proposal receipts matched. This deterministic fixture
test is not evidence of fresh AI reasoning or customer-ready visual quality.
The real modal reused the saved credential, but its first live request was rejected
because the provider account had no credits. Fresh AI end-to-end verification
therefore remains blocked; recorded-provider tests must be labelled separately.
A recorded-provider modal run subsequently passed one batch approval and all five
capture approvals through the real session/tool/native stack. Its saved/reopened
result matched IDs, scales, clipping scope and receipts. This validates plumbing,
not model judgement. All five previews were inspected; missing labels, grey
backgrounds and unwanted site lines remain presentation defects.

The executor does not yet provide page captions, dedicated detail
layer placement, per-detail visibility, full visual preview, persistent partial-phase
resume, in-batch cancellation or native Undo for page creation. Do not enable it as the
customer default until native failure recovery and the remaining release gates pass. The
existing staged tools remain available.

## Next implementation slices

1. Model-readiness inventory: units and clipping-widget-free model bounds are exposed.
   Extend this to selection, block/layer context, proposed levels and ambiguities.
2. Compiler/executor: resolve logical keys to native resources, enforce physical scale,
   set per-detail visibility and clipping, and verify actual native results. Do not
   simply concatenate existing single-operation plans: their prerequisites and
   revision semantics differ.
3. Approval: render the complete proposal and share scope. Bind approval to an immutable
   proposal digest and source revision. New scope requires approval again.
4. Recovery: persistent execution journal, duplicate prevention, phase checkpoints,
   explicit partial-failure results, safe rollback and user-controlled resume.
5. Quality: captions, scale labels, templates, monochrome presentation, full-sheet plus
   detail-crop inspection, and deterministic scale/visibility/clipping checks.
6. Modal integration: Stop and explicit Retry request controls are implemented;
   uncertain approval failures resolve their tool call with an uncertainty result.
   Reviewed screenshot history is pruned by default. Still verify native in-batch
   cancellation, restart-safe recovery and selective corrections.
7. Distribution: entitlement implementation, supported-platform secure storage,
   compatibility checks, packaging and redacted support diagnostics.

## Release gates (not yet met)

- Repeat the house workflow entirely in the modal without developer scripts or repair.
- Complete a representative fixture matrix: blocks/meshes, irregular/sloping models,
  multiple buildings, large coordinates, hidden layers and incomplete metadata.
- Prove stale approval rejection, repeat-safe resume, cancellation, provider quota
  failure and save/reopen persistence without unrelated document mutation.
- Verify scale, frame, labels, intended floor/direction and viewport-specific clipping
  on every result. No success status before native postconditions pass.
- Measure latency and actual returned token usage on clean runs; set performance
  targets from that benchmark, not the debugging-heavy first demo.
- Verify every advertised operating system and supported Rhino/shared-UI version.

Unit-test success alone is not a v1 release decision.
