using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public static class ExistingSheetLayoutClassifier
{
    public static BuiltInLayoutKind? Classify(IReadOnlyList<DetailSnapshot> details)
    {
        ArgumentNullException.ThrowIfNull(details);

        return details.Count switch
        {
            0 => BuiltInLayoutKind.Blank,
            1 => BuiltInLayoutKind.SingleDetail,
            2 => ClassifyTwoDetails(details[0].PageBounds, details[1].PageBounds),
            4 => BuiltInLayoutKind.FourDetailsGrid,
            _ => null,
        };
    }

    private static BuiltInLayoutKind? ClassifyTwoDetails(
        DetailPageBounds? first,
        DetailPageBounds? second)
    {
        if (first is not { IsValid: true } || second is not { IsValid: true })
            return null;

        var horizontalSeparation = Math.Abs(first.CenterX - second.CenterX);
        var verticalSeparation = Math.Abs(first.CenterY - second.CenterY);
        if (horizontalSeparation > verticalSeparation)
            return BuiltInLayoutKind.TwoDetailsVertical;
        if (verticalSeparation > horizontalSeparation)
            return BuiltInLayoutKind.TwoDetailsHorizontal;

        return null;
    }
}
