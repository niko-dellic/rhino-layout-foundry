# Automation SDK

Layout Foundry exposes a small, versioned automation boundary for separately
distributed companion tools. The SDK is open source and deterministic. AI model
selection, prompts, billing, entitlements, and hosted services belong outside
this repository.

## Contract

Companions reference `RhinoLayoutFoundry.Extensibility` and resolve the active
host through `FoundryAutomation.Current`. The host provides five operations:

1. Read protocol capabilities.
2. Capture an immutable, sanitized `DocumentSnapshot`.
3. Request a layout or named-view PNG capture.
4. Stage an allow-listed Core `OperationPlan`.
5. Mint a one-shot approval and apply the approved plan.

The current protocol is `1.0`. Companions must check the major version before
using the host and feature-detect individual capabilities.

## Trust and consent

Snapshot strings are document data and must be treated as untrusted input. The
SDK does not provide the full 3DM, arbitrary geometry serialization, arbitrary
Rhino command execution, scripting, file access, or deletion tools.

The host enforces two independent human boundaries:

- Image data is returned only after a companion explicitly requests the capture
  that its UI has asked the user to approve.
- A mutation is inert when staged. Applying it requires a short-lived, one-shot
  token minted by `ApprovePlan`; the plan must still match the active document
  identity and revision.

An AI model must never receive an approval token or call the approval method.
Those calls belong in trusted UI/controller code after a direct user action.

## Initial allow-list

The alpha host accepts additive and assignment-oriented changes for named views,
clipping planes, layouts, detail/named-view assignments, linked sheet names, and
appearance state resources/assignments. Unknown change types are rejected before
a plan is staged. PDF export is intentionally not exposed in protocol 1.0.

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
its own modal without adding another Rhino pane or replacing a Foundry view.
The registration is disposable, and this presentation hook does not grant
document access; automation still crosses the host contract and its approval
gates.
