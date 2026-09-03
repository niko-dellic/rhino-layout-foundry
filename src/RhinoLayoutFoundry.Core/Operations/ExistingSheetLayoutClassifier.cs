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

    public static IReadOnlyList<DetailSnapshot> OrderForLayout(
        IReadOnlyList<DetailSnapshot> details,
        BuiltInLayoutKind layout)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.Count < 2 || details.Any(detail => detail.PageBounds is not { IsValid: true }))
            return details.ToArray();

        return layout switch
        {
            BuiltInLayoutKind.TwoDetailsVertical => details
                .OrderBy(detail => detail.PageBounds!.CenterX)
                .ToArray(),
            BuiltInLayoutKind.TwoDetailsHorizontal => details
                .OrderByDescending(detail => detail.PageBounds!.CenterY)
                .ToArray(),
            BuiltInLayoutKind.FourDetailsGrid => details
                .OrderByDescending(detail => detail.PageBounds!.CenterY)
                .ThenBy(detail => detail.PageBounds!.CenterX)
                .ToArray(),
            _ => details.ToArray(),
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
