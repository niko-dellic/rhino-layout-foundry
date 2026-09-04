# Current architecture

## Project boundaries

| Project | Owns | Must not own |
| --- | --- | --- |
| Core | Domain records, planners, naming, hierarchy, serialization, archive format, compensation and preview scheduling policies | Rhino/Eto objects or UI dispatch |
| Rhino | Plug-in lifecycle, native resources, persistence envelope, mutation execution, capture and export | Controls stored in document state |
| UI | Eto views, shared Foundry controls, draft sessions and application workflows | Direct mutation of Rhino documents |
| Extensibility | Experimental contracts and staged approval registry | Provider credentials, AI logic or native geometry |
| Core.Tests | Domain and host-independent boundary regressions | Claims of native Rhino verification |
| Foundry.ReleaseCheck | Candidate binary/version/platform checks and deterministic staging allowlist | Publishing or loading Rhino assemblies |

## Application and mutation flow

`LayoutFoundryPlugin` composes native adapters into one `FoundryApplicationService` through `LayoutFoundryUiHost`. The facade retains the existing UI entry points. The service instance owns providers, selection and notifications. Its workflow modules cover hierarchy, appearance and sheets; a shared runner captures snapshots and handles expected failure/cancellation.

Views create requests from staged values. Core planners produce an `OperationPlan` with document serial, source revision, changes and diagnostics. The host mutation service dispatches to the UI thread, rejects unavailable/inactive/protected documents and stale plans, then calls `RhinoMutationExecutor`. Family modules share one executor's state store, revision tracker and notification boundary. Private helpers remain within that owner; these modules are not separate public services.

Undo support depends on the native operation. Metadata uses custom Undo state; native operations use available Rhino records and compensating restoration. Non-undoable operations remain explicit limitations. The live test matrix is authoritative for verified support. A successful return must not be described as full recovery when cleanup failures exist.

## Persistence

The 3DM plug-in archive contains an ArchivableDictionary with `SchemaVersion` and a JSON `Payload`. DocumentStateSerializer validates the single current format: schema **16**. Package archives accept format **6** only. Other versions are rejected without conversion. Required collections have one serialized name, are initialized explicitly for new state, and reject missing/null stored values. Unknown state fields are rejected so editing cannot silently discard them.

DocumentStateStore is UI-thread-owned and separates Loaded, Unsupported and Invalid states. It retains the original envelope after reading, including unknown fields. Snapshot reads do not prune metadata or set Rhino's modified flag. A successful intentional state mutation drops that original envelope and writes the current schema on the next save. Explicit reconciliation handles stale template sources, appearance rules, and assignments at relevant mutation boundaries.

Protected states allow overview/navigation but reject Foundry mutation before native writes. The panel displays a persistent diagnostic. Recoverable original envelopes pass through unchanged when saving; an entirely unreadable archive must not be replaced with empty metadata. Recovery guidance is in RECOVERY.md.

## Resource lifetimes

`RhinoPreviewSession` registers a temporary page immediately after acquisition, before details or title blocks are constructed. It attempts all cleanup and restores Undo recording even after another cleanup step fails. It owns no delayed dirty-flag resets. A canceled preview can leave the document marked modified because native teardown cannot reliably distinguish preview changes from real edits.

`BatchLayoutSession` owns dialog drafts, cancellation sources, and separate coalescing schedulers for creation/edit previews. Render callbacks reject stale results before updating controls. Gallery controls own and dispose their images. Native Rhino captures still pass through the shared capture gate. The dialog owns close cancellation and `PreviewCleanup`; modal callers await that task. `WaitForPendingCapturesAsync` is a completion barrier, with no session flags or deferred dirty-state restoration.

`RhinoImportTransaction` journals temporary pages, display modes, new definitions/materials/line styles, overwritten named views/layer states, and metadata. Rollback runs in reverse resource order, attempts all actions, and reports failures. A recovery package is captured before dependencies are imported; Replace restoration uses that package after cutover. Recreated native IDs may differ.

## Interface contracts

- Snapshots contain Core values and IDs, never retained Rhino controls or document handles.
- Native adapters execute document access on Rhino's UI thread. Core policies do not marshal threads.
- Live identity uses document runtime serial and source revision. Sheet IDs are main viewport IDs; detail IDs are viewport IDs, not detail object IDs.
- Page dimensions and scales include explicit unit context. Never infer millimeters from a bare number.
- Cancellation is cooperative at safe boundaries. Native calls may complete before cancellation can be observed.
- CompensationJournal owns inverse actions in acquisition order; callers register immediately and handle incomplete rollback.
- The experimental automation registry freezes staged plans, checks document identity/revision and expiry, and consumes approval tokens once. The trusted companion remains responsible for obtaining user consent. In-process plug-ins are not a security sandbox.

## Build and documentation contracts

Version.props owns the release version. global.json and Directory.Packages.props plus lock files own the build inputs. Platform markers are embedded in assemblies and verified during staging. Portable CI cannot establish native Mac behavior.

README describes the product as it exists. TESTING_AND_RELEASE describes acceptance evidence. PRODUCT_SPEC describes intent. Historical records are in history/; their sample schemas and milestone statuses are not canonical APIs.

## Templates, appearance, and title blocks

`LayoutTemplateRegistration` is an identity plus a live sheet/detail scope. The checkbox registers that source; folders cannot be templates. Snapshot capture derives `SheetTemplateRecipe` values from the source's current paper and detail geometry. These recipes are transient planning inputs, never a second persisted library. Deleting a source removes its registration at explicit reconciliation. No capability links or cached linked payloads remain.

Appearance states and local hierarchy rules are separate from layout templates. Their existing resolver establishes inherited and local layer/object display behavior. The former display-rule selector system and tags do not exist in the current model.

Foundry manages only built-in Right and Bottom blocks (None removes its managed block). `TitleBlockRole` records the native instance/definition IDs and built-in kind. Project information and per-sheet revision schedules supply content. Ordinary Rhino blocks remain ordinary page geometry, transported with their definition dependencies in packages; import does not classify them as managed blocks.

Creation accepts a required `CreationSpecs` collection. Each specification owns its paper size/units, quantity, optional live template identity, built-in title-block choice, and ordered per-detail named-view assignments. A null per-detail entry preserves the source camera; assignment counts must match resolved slots. The UI and experimental automation use this same planner contract. There are no quantity-only, singular-view, or request-wide assignment fallbacks.
