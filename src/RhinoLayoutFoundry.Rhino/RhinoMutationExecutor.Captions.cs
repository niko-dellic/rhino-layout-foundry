using Rhino;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Diagnostics;

namespace RhinoLayoutFoundry.Rhino;

internal sealed partial class RhinoMutationExecutor
{
    private OperationResult ApplyDetailCaptions(RhinoDoc document, OperationPlan plan, SetDetailCaptionsChange change)
    {
        var before = _stateStore.Get(document);
        var undo = document.BeginUndoRecord(plan.UndoDescription);
        if (undo == 0) return Failure("operation.undo_unavailable", "Could not start undo record.");
        try
        {
            if (!document.AddCustomUndoEvent(plan.UndoDescription, OnUndoDocumentState,
                    new DocumentStateUndoTag(plan.UndoDescription, before)))
                return Failure("operation.undo_unavailable", "Could not register caption Undo.");
            var after = before with { Metadata = DetailCaptions.Write(before.Metadata, change.Scope, change.Settings) };
            _stateStore.Set(document, after);
            RhinoDetailCaptionService.Synchronize(document, after);
            document.Modified = true;
            _revisionTracker.Bump(document);
            return SuccessWithEntity(plan, "caption.updated", "Updated detail captions.", change.Scope.Id);
        }
        finally { document.EndUndoRecord(undo); }
    }
}
