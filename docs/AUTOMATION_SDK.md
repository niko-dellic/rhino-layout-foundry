# Experimental companion API

Layout Foundry exposes an experimental, versioned automation boundary for separately
distributed companion tools. The SDK is open source. Core record shapes and this API may change before a stable SDK is announced. AI model
selection, prompts, billing, entitlements, and hosted services belong outside
this repository.

## Contract

Companions reference `RhinoLayoutFoundry.Extensibility` and resolve the active
host through `FoundryAutomation.Current`. The host provides five operations:

1. Read protocol capabilities.
2. Capture a host-independent `DocumentSnapshot`. Its strings can include project information.
3. Request a layout or named-view PNG capture.
4. Stage an allow-listed Core `OperationPlan`.
5. Mint a one-shot approval and apply the approved plan.

The current protocol is `2.0`. Companions must check the major version before
using the host and feature-detect individual capabilities.

## Trust and consent

Snapshot strings are document data and must be treated as untrusted input. The
SDK does not provide the full 3DM, arbitrary geometry serialization, arbitrary
Rhino command execution, scripting, file access, or deletion tools.

The companion is trusted in-process code, not a sandboxed client. It must obtain
user consent before requesting images, sharing document data, or calling
`ApprovePlan`. The host does not display or cryptographically verify that human
consent. Any in-process caller with access to the interface can call approval.

Staging freezes a defensive copy of the proposed changes. Applying requires a
short-lived, one-shot token; identity and revision are checked at staging,
approval and application, and again by the native mutation service. Protected
metadata and unsupported operations remain blocked. Failed/canceled application
consumes the token and requires a new review and approval.

An AI model must never receive an approval token or call the approval method.
Those calls belong in trusted UI/controller code after a direct user action.

## Initial allow-list

The alpha host accepts additive and assignment-oriented changes for named views,
clipping planes, layouts, detail/named-view assignments, linked sheet names, and
appearance state resources/assignments. Unknown change types are rejected before
a plan is staged. PDF export is intentionally not exposed in protocol 2.0.

## Example flow

```csharp
var host = FoundryAutomation.Current
    ?? throw new InvalidOperationException("Layout Foundry is not loaded.");

var snapshot = host.CaptureSnapshot();
var plan = planner.Plan(request, snapshot);
var staged = host.StagePlan(plan);       // no document change

// A trusted companion UI presents staged.Summary and waits for a user click.
var approval = host.ApprovePlan(staged.PlanId);
var result = await host.ApplyApprovedPlanAsync(approval, cancellationToken);
```

Plans expire after fifteen minutes in the current Rhino host. Approval tokens are
cryptographically random, compared in constant time, single-use, and bound to one
staged plan.

## Optional create-menu integration

A separately distributed Eto companion can register a
`FoundryCreateMenuAction` through `FoundryCreateMenuActions.Register`. Layout
Foundry renders the contributed label and icon in its existing `+` menu and
invokes the action with the panel as its owner. A companion can therefore open
its in-panel workspace without adding another Rhino pane or replacing a Foundry view.
The registration is disposable, and this presentation hook does not grant
document access; automation still crosses the host contract and its approval
gates.

## Drawing creation contract

AI batch creation uses `stage_drawing_set` with drawing-set specification v2. Required fields, including each view's `hidden_layer_ids` array (empty is allowed), must be explicit. The JSON request envelope includes `protocol_major: 2`; incompatible pairs must update both components and restart Rhino before a task begins. Unknown tools, specification v1 and missing fields are rejected before staging.

Drawing proposals contain explicit sheets and views, exact scale, cameras, page bounds and optional cuts, with reserved title/caption space. Inspect current document revision and resource IDs first. The current schema is defined by `DrawingSetSpecification` and validated by `DrawingSetSpecificationValidator`. Native mutation occurs only after a reviewed plan receives one-shot approval. Normal basic sheet creation and existing correction operations remain available.

Document schema 17 and package format 6 remain current. Unsupported metadata is preserved without migration or automatic reset. Conversations use checkpoint v2 with typed presentation events separate from provider history; checkpoint v1 is unsupported. See [clean-break validation](PRE_BETA_CLEAN_BREAK.md).
