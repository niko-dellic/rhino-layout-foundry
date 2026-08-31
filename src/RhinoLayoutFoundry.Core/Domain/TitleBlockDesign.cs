namespace RhinoLayoutFoundry.Core.Domain;

public enum BuiltInTitleBlockKind
{
    CompactLowerRight,
    FullWidthBottom,
    RightSidebar,
    MinimalLowerRight,
}

public sealed record ProjectInformation(
    string ProjectName,
    string ProjectNumber,
    string ClientName,
    string SiteAddress,
    string ProjectPhase,
    string ProjectStatus,
    string FirmName,
    string FirmAddress,
    string FirmPhone,
    string FirmEmail,
    string FirmWebsite,
    string FirmRegistration,
    string IssueDate,
    string IssuePurpose,
    string DrawnBy,
    string CheckedBy,
    string ApprovedBy,
    IReadOnlyDictionary<string, string> CustomFields,
    BrandAsset? Logo = null,
    SheetRevisionRecord? DefaultRevision = null)
{
    public static ProjectInformation Empty { get; } = new(
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

public sealed record BrandAsset(
    string FileName,
    string MediaType,
    string Sha256,
    byte[] Data);

public sealed record SheetRevisionRecord(
    string Code,
    string Date,
    string Description,
    string IssuedBy,
    string CheckedBy);

public sealed record SheetTitleBlockData(
    string SheetNumber,
    IReadOnlyList<SheetRevisionRecord> Revisions,
    IReadOnlyDictionary<string, string>? CustomFields = null)
{
    public IReadOnlyDictionary<string, string> Custom =>
        CustomFields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public readonly record struct TitleBlockRectangle(double Left, double Bottom, double Width, double Height)
{
    public double Right => Left + Width;
    public double Top => Bottom + Height;
}

public sealed record AdaptiveTitleBlockLayout(
    BuiltInTitleBlockKind Kind,
    TitleBlockRectangle Page,
    TitleBlockRectangle Block,
    TitleBlockRectangle Content,
    double Margin,
    double Gutter,
    double BodyTextHeight,
    double HeadingTextHeight,
    bool IsCompact,
    int VisibleRevisionRows,
    string Signature);

public static class AdaptiveTitleBlockLayoutSolver
{
    public const int StyleVersion = 4;

    public static AdaptiveTitleBlockLayout Solve(BuiltInTitleBlockKind kind, PaperRecipe paper)
    {
        ArgumentNullException.ThrowIfNull(paper);
        if (paper.Width <= 0 || paper.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(paper), "Paper dimensions must be positive.");

        var unitsPerMillimeter = UnitsPerMillimeter(paper.UnitSystem);
        var widthMm = paper.Width / unitsPerMillimeter;
        var heightMm = paper.Height / unitsPerMillimeter;
        var shortMm = Math.Min(widthMm, heightMm);
        var marginMm = Clamp(shortMm * 0.025, 5, 15);
        var gutterMm = Clamp(shortMm * 0.01, 2.5, 6);
        var bodyMm = Clamp(shortMm * 0.012, 2.5, 4);
        var headingMm = bodyMm * 1.4;
        var compact = shortMm < 300;

        double blockWidthMm;
        double blockHeightMm;
        double blockLeftMm;
        double blockBottomMm = marginMm;
        switch (kind)
        {
            case BuiltInTitleBlockKind.CompactLowerRight:
                blockWidthMm = Clamp(widthMm * 0.38, 90, 260);
                blockHeightMm = Clamp(heightMm * 0.20, 42, 90);
                blockLeftMm = widthMm - marginMm - blockWidthMm;
                break;
            case BuiltInTitleBlockKind.FullWidthBottom:
                blockWidthMm = widthMm - marginMm * 2;
                blockHeightMm = Clamp(heightMm * 0.16, 42, 82);
                blockLeftMm = marginMm;
                break;
            case BuiltInTitleBlockKind.RightSidebar:
                blockWidthMm = Clamp(widthMm * 0.18, 55, 110);
                blockHeightMm = heightMm - marginMm * 2;
                blockLeftMm = widthMm - marginMm - blockWidthMm;
                break;
            case BuiltInTitleBlockKind.MinimalLowerRight:
                blockWidthMm = Clamp(widthMm * 0.30, 75, 190);
                blockHeightMm = Clamp(heightMm * 0.13, 35, 65);
                blockLeftMm = widthMm - marginMm - blockWidthMm;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        if (blockWidthMm <= 0 || blockHeightMm <= 0 || blockLeftMm < marginMm - 0.001)
            throw new InvalidOperationException("The paper is too small for this title-block family.");

        double contentLeftMm = marginMm;
        double contentBottomMm = marginMm;
        double contentWidthMm;
        double contentHeightMm;
        if (kind == BuiltInTitleBlockKind.RightSidebar)
        {
            contentWidthMm = blockLeftMm - gutterMm - marginMm;
            contentHeightMm = heightMm - marginMm * 2;
        }
        else
        {
            contentBottomMm = blockBottomMm + blockHeightMm + gutterMm;
            contentWidthMm = widthMm - marginMm * 2;
            contentHeightMm = heightMm - marginMm - contentBottomMm;
        }

        if (contentWidthMm < 25 || contentHeightMm < 25)
            throw new InvalidOperationException("The title block leaves no usable drawing area on this paper size.");

        var rowHeightMm = Math.Max(bodyMm * 2.8, 8);
        var reservedMm = compact ? 28 : 34;
        var rows = Math.Max(1, (int)Math.Floor((blockHeightMm - reservedMm) / rowHeightMm));
        rows = Math.Min(compact ? 3 : 6, rows);
        var signature = FormattableString.Invariant(
            $"tb{StyleVersion}:{kind}:{widthMm:0.##}x{heightMm:0.##}:{blockWidthMm:0.##}x{blockHeightMm:0.##}");

        double U(double millimeters) => millimeters * unitsPerMillimeter;
        return new AdaptiveTitleBlockLayout(
            kind,
            new TitleBlockRectangle(0, 0, paper.Width, paper.Height),
            new TitleBlockRectangle(U(blockLeftMm), U(blockBottomMm), U(blockWidthMm), U(blockHeightMm)),
            new TitleBlockRectangle(U(contentLeftMm), U(contentBottomMm), U(contentWidthMm), U(contentHeightMm)),
            U(marginMm),
            U(gutterMm),
            U(bodyMm),
            U(headingMm),
            compact,
            rows,
            signature);
    }

    public static string Label(BuiltInTitleBlockKind kind) => kind switch
    {
        BuiltInTitleBlockKind.CompactLowerRight => "Compact lower-right",
        BuiltInTitleBlockKind.FullWidthBottom => "Full-width bottom band",
        BuiltInTitleBlockKind.RightSidebar => "Right-side vertical",
        BuiltInTitleBlockKind.MinimalLowerRight => "Minimal lower-right",
        _ => kind.ToString(),
    };

    private static double UnitsPerMillimeter(string unitSystem) => unitSystem?.Trim() switch
    {
        "Millimeters" or "Millimeter" => 1,
        "Centimeters" or "Centimeter" => 0.1,
        "Meters" or "Meter" => 0.001,
        "Inches" or "Inch" => 1 / 25.4,
        "Feet" or "Foot" => 1 / 304.8,
        _ => throw new ArgumentException($"Unsupported paper unit system '{unitSystem}'.", nameof(unitSystem)),
    };

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));
}
