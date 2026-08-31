using System.Security.Cryptography;
using System.Text;

namespace RhinoLayoutFoundry.Core.Domain;

public enum TitleBlockContentField
{
    ProjectName,
    ProjectNumber,
    ClientName,
    SiteAddress,
    ProjectPhase,
    ProjectStatus,
    FirmName,
    FirmAddress,
    FirmPhone,
    FirmEmail,
    FirmWebsite,
    FirmRegistration,
    Logo,
    IssueDate,
    IssuePurpose,
    DrawnBy,
    CheckedBy,
    ApprovedBy,
}

public sealed record CustomTitleBlockFieldOption(
    string Label,
    bool IsIncluded = true);

public sealed record TitleBlockContentOptions(
    IReadOnlyList<TitleBlockContentField> IncludedFields,
    IReadOnlyList<CustomTitleBlockFieldOption> CustomFields,
    bool ReserveRevisionArea = false)
{
    public static IReadOnlyList<TitleBlockContentField> ConventionalFields { get; } =
    [
        TitleBlockContentField.ProjectName,
        TitleBlockContentField.ProjectNumber,
        TitleBlockContentField.ClientName,
        TitleBlockContentField.SiteAddress,
        TitleBlockContentField.ProjectPhase,
        TitleBlockContentField.ProjectStatus,
        TitleBlockContentField.FirmName,
        TitleBlockContentField.FirmAddress,
        TitleBlockContentField.FirmPhone,
        TitleBlockContentField.FirmEmail,
        TitleBlockContentField.FirmWebsite,
        TitleBlockContentField.Logo,
        TitleBlockContentField.IssueDate,
        TitleBlockContentField.IssuePurpose,
        TitleBlockContentField.DrawnBy,
        TitleBlockContentField.CheckedBy,
    ];

    public static TitleBlockContentOptions Default(
        IReadOnlyDictionary<string, string>? customFields = null) => new(
        ConventionalFields,
        (customFields ?? new Dictionary<string, string>())
            .Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(value => new CustomTitleBlockFieldOption(value))
            .ToArray());

    public bool Includes(TitleBlockContentField field) => IncludedFields.Contains(field);

    public TitleBlockContentOptions Normalize(IReadOnlyDictionary<string, string> customValues)
    {
        var included = IncludedFields.Distinct().ToArray();
        var custom = new List<CustomTitleBlockFieldOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in CustomFields)
        {
            var label = option.Label?.Trim() ?? string.Empty;
            if (label.Length == 0 || !customValues.ContainsKey(label) || !seen.Add(label)) continue;
            custom.Add(option with { Label = label });
        }
        foreach (var label in customValues.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            if (seen.Add(label)) custom.Add(new CustomTitleBlockFieldOption(label));
        return this with { IncludedFields = included, CustomFields = custom };
    }
}

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
    SheetRevisionRecord? DefaultRevision = null,
    TitleBlockContentOptions? TitleBlockOptions = null)
{
    public static ProjectInformation Empty { get; } = new(
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public TitleBlockContentOptions ContentOptions =>
        (TitleBlockOptions ?? TitleBlockContentOptions.Default(CustomFields)).Normalize(CustomFields);
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

public enum TitleBlockFieldStyle
{
    Standard,
    Prominent,
    SheetNumber,
}

public sealed record TitleBlockFieldPlacement(
    string Key,
    string Label,
    TitleBlockRectangle Bounds,
    TitleBlockFieldStyle Style = TitleBlockFieldStyle.Standard);

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
    string Signature,
    IReadOnlyList<TitleBlockFieldPlacement> Fields,
    TitleBlockRectangle? LogoRegion,
    TitleBlockRectangle? RevisionRegion);

public static class AdaptiveTitleBlockLayoutSolver
{
    public const int StyleVersion = 5;

    public static AdaptiveTitleBlockLayout Solve(BuiltInTitleBlockKind kind, PaperRecipe paper) =>
        Solve(kind, paper, ProjectInformation.Empty, detailCount: 1);

    public static AdaptiveTitleBlockLayout Solve(
        BuiltInTitleBlockKind kind,
        PaperRecipe paper,
        ProjectInformation project) => Solve(kind, paper, project, detailCount: 1);

    public static AdaptiveTitleBlockLayout Solve(
        BuiltInTitleBlockKind kind,
        PaperRecipe paper,
        ProjectInformation project,
        int detailCount)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(project);
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
        var normalizedKind = kind == BuiltInTitleBlockKind.FullWidthBottom
            ? BuiltInTitleBlockKind.FullWidthBottom
            : BuiltInTitleBlockKind.RightSidebar;
        var includeScale = detailCount == 1;
        var options = project.ContentOptions;
        var descriptors = FieldDescriptors(project, options).ToArray();
        var composition = normalizedKind == BuiltInTitleBlockKind.RightSidebar
            ? ComposeRight(widthMm, heightMm, marginMm, gutterMm, bodyMm, project, options, descriptors,
                includeScale)
            : ComposeBottom(widthMm, heightMm, marginMm, gutterMm, bodyMm, project, options, descriptors,
                includeScale);
        var signature = Signature(normalizedKind, widthMm, heightMm, composition.Block, project, options,
            includeScale);

        double U(double value) => value * unitsPerMillimeter;
        TitleBlockRectangle R(TitleBlockRectangle rectangle) => new(
            U(rectangle.Left), U(rectangle.Bottom), U(rectangle.Width), U(rectangle.Height));
        return new AdaptiveTitleBlockLayout(
            normalizedKind,
            new TitleBlockRectangle(0, 0, paper.Width, paper.Height),
            R(composition.Block),
            R(composition.Content),
            U(marginMm),
            U(gutterMm),
            U(bodyMm),
            U(headingMm),
            compact,
            0,
            signature,
            composition.Fields.Select(field => field with { Bounds = R(field.Bounds) }).ToArray(),
            composition.Logo is { } logo ? R(logo) : null,
            composition.Revision is { } revision ? R(revision) : null);
    }

    public static string Label(BuiltInTitleBlockKind kind) => kind switch
    {
        BuiltInTitleBlockKind.FullWidthBottom => "Bottom",
        BuiltInTitleBlockKind.RightSidebar => "Right",
        BuiltInTitleBlockKind.CompactLowerRight => "Right (legacy compact)",
        BuiltInTitleBlockKind.MinimalLowerRight => "Right (legacy minimal)",
        _ => kind.ToString(),
    };

    private static Composition ComposeRight(
        double pageWidth,
        double pageHeight,
        double margin,
        double gutter,
        double body,
        ProjectInformation project,
        TitleBlockContentOptions options,
        IReadOnlyList<FieldDescriptor> descriptors,
        bool includeScale)
    {
        var blockHeight = pageHeight - margin * 2;
        var inset = Math.Max(gutter * 0.55, body * 0.7);
        var logoHeight = options.Includes(TitleBlockContentField.Logo) && project.Logo is not null ? 28d : 0;
        var status = descriptors.FirstOrDefault(item => item.Field == TitleBlockContentField.ProjectStatus);
        var projectName = descriptors.FirstOrDefault(item => item.Field == TitleBlockContentField.ProjectName);
        var projectNumber = descriptors.FirstOrDefault(item => item.Field == TitleBlockContentField.ProjectNumber);
        var flexible = descriptors.Where(item => item != status && item != projectName && item != projectNumber).ToArray();
        var statusHeight = status is null ? 0 : 15;
        var projectHeight = projectName is null ? 0 : 18;
        var projectNumberHeight = projectNumber is null ? 0 : 10;
        var sheetHeight = includeScale ? 39d : 31d;
        var revisionHeight = options.ReserveRevisionArea ? Clamp(blockHeight * 0.25, 45, 110) : 0;
        var fixedHeight = inset * 2 + logoHeight + statusHeight + revisionHeight + projectHeight +
                          projectNumberHeight + sheetHeight;
        var flexibleHeight = blockHeight - fixedHeight;
        if (flexibleHeight < 10 && flexible.Length > 0)
            throw new InvalidOperationException("The enabled title-block fields do not fit this paper size.");

        var maximumWidth = pageWidth - margin * 2 - gutter - 25;
        var columns = RequiredColumns(flexible, flexibleHeight, Math.Max(1, (int)Math.Floor(maximumWidth / 56)));
        var blockWidth = Clamp(72 + (columns - 1) * 56, 65, maximumWidth);
        if (blockWidth < 55 || maximumWidth < 55)
            throw new InvalidOperationException("The title block leaves less than 25 mm of usable drawing width.");
        var block = new TitleBlockRectangle(pageWidth - margin - blockWidth, margin, blockWidth, blockHeight);
        var fields = new List<TitleBlockFieldPlacement>();
        var y = block.Top - inset;
        TitleBlockRectangle? logo = null;
        if (logoHeight > 0)
        {
            logo = new TitleBlockRectangle(block.Left + inset, y - logoHeight, block.Width - inset * 2, logoHeight);
            y -= logoHeight;
        }
        if (status is not null)
        {
            fields.Add(Placement(status, block.Left, y - statusHeight, block.Width, statusHeight));
            y -= statusHeight;
        }

        var flexibleBottom = y - flexibleHeight;
        PackColumns(fields, flexible, block.Left, flexibleBottom, block.Width, flexibleHeight, columns);
        y = flexibleBottom;
        TitleBlockRectangle? revision = null;
        if (revisionHeight > 0)
        {
            revision = new TitleBlockRectangle(block.Left, y - revisionHeight, block.Width, revisionHeight);
            y -= revisionHeight;
        }
        if (projectName is not null)
        {
            fields.Add(Placement(projectName, block.Left, y - projectHeight, block.Width, projectHeight));
            y -= projectHeight;
        }
        if (projectNumber is not null)
        {
            fields.Add(Placement(projectNumber, block.Left, y - projectNumberHeight, block.Width, projectNumberHeight));
            y -= projectNumberHeight;
        }
        fields.Add(new TitleBlockFieldPlacement("sheet.title", "Sheet title",
            new TitleBlockRectangle(block.Left, y - 12, block.Width, 12)));
        y -= 12;
        fields.Add(new TitleBlockFieldPlacement("sheet.number", "Sheet no.",
            new TitleBlockRectangle(block.Left, y - 19, block.Width, 19), TitleBlockFieldStyle.SheetNumber));
        y -= 19;
        if (includeScale)
            fields.Add(new TitleBlockFieldPlacement("sheet.scale", "Scale",
                new TitleBlockRectangle(block.Left, y - 8, block.Width, 8)));
        var content = new TitleBlockRectangle(margin, margin, block.Left - gutter - margin, blockHeight);
        return new Composition(block, content, fields, logo, revision);
    }

    private static Composition ComposeBottom(
        double pageWidth,
        double pageHeight,
        double margin,
        double gutter,
        double body,
        ProjectInformation project,
        TitleBlockContentOptions options,
        IReadOnlyList<FieldDescriptor> descriptors,
        bool includeScale)
    {
        var blockWidth = pageWidth - margin * 2;
        var inset = Math.Max(gutter * 0.55, body * 0.7);
        var hasLogo = options.Includes(TitleBlockContentField.Logo) && project.Logo is not null;
        var logoWidth = hasLogo ? Clamp(blockWidth * 0.18, 42, 76) : 0;
        var revisionWidth = options.ReserveRevisionArea ? Clamp(blockWidth * 0.25, 70, 180) : 0;
        var sheetWidth = Clamp(blockWidth * 0.22, 58, 92);
        var informationWidth = blockWidth - logoWidth - revisionWidth - sheetWidth;
        if (informationWidth < 48 && descriptors.Count > 0)
            throw new InvalidOperationException("The enabled title-block regions do not fit this paper width.");
        var columns = Math.Max(1, (int)Math.Floor(Math.Max(48, informationWidth) / 52));
        var informationHeight = RequiredColumnHeight(descriptors, columns);
        var blockHeight = Math.Max(55, informationHeight + inset * 2);
        var maximumHeight = pageHeight - margin * 2 - gutter - 25;
        if (blockHeight > maximumHeight)
        {
            columns = Math.Max(columns, (int)Math.Ceiling(descriptors.Sum(item => item.Height) /
                Math.Max(10, maximumHeight - inset * 2)));
            if (informationWidth / columns < 42)
                throw new InvalidOperationException("The enabled title-block fields leave less than 25 mm of drawing height.");
            informationHeight = RequiredColumnHeight(descriptors, columns);
            blockHeight = informationHeight + inset * 2;
        }
        if (blockHeight > maximumHeight || maximumHeight < 42)
            throw new InvalidOperationException("The enabled title-block fields leave less than 25 mm of drawing height.");

        var block = new TitleBlockRectangle(margin, margin, blockWidth, blockHeight);
        var fields = new List<TitleBlockFieldPlacement>();
        var x = block.Left;
        TitleBlockRectangle? logo = null;
        if (hasLogo)
        {
            logo = new TitleBlockRectangle(x + inset, block.Bottom + inset,
                logoWidth - inset * 2, block.Height - inset * 2);
            x += logoWidth;
        }
        PackColumns(fields, descriptors, x, block.Bottom, informationWidth, block.Height, columns);
        x += informationWidth;
        TitleBlockRectangle? revision = null;
        if (revisionWidth > 0)
        {
            revision = new TitleBlockRectangle(x, block.Bottom, revisionWidth, block.Height);
            x += revisionWidth;
        }
        fields.Add(new TitleBlockFieldPlacement("sheet.title", "Sheet title",
            new TitleBlockRectangle(x, block.Top - 14, sheetWidth, 14)));
        var sheetNumberBottom = includeScale ? block.Bottom + 11 : block.Bottom;
        fields.Add(new TitleBlockFieldPlacement("sheet.number", "Sheet no.",
            new TitleBlockRectangle(x, sheetNumberBottom, sheetWidth, block.Top - 14 - sheetNumberBottom),
            TitleBlockFieldStyle.SheetNumber));
        if (includeScale)
            fields.Add(new TitleBlockFieldPlacement("sheet.scale", "Scale",
                new TitleBlockRectangle(x, block.Bottom, sheetWidth, 11)));
        var contentBottom = block.Top + gutter;
        var content = new TitleBlockRectangle(margin, contentBottom, blockWidth, pageHeight - margin - contentBottom);
        return new Composition(block, content, fields, logo, revision);
    }

    private static IEnumerable<FieldDescriptor> FieldDescriptors(
        ProjectInformation project,
        TitleBlockContentOptions options)
    {
        FieldDescriptor? Field(TitleBlockContentField field, string key, string label,
            double height = 10, TitleBlockFieldStyle style = TitleBlockFieldStyle.Standard) =>
            options.Includes(field) ? new FieldDescriptor(field, key, label, height, style) : null;
        var fields = new FieldDescriptor?[]
        {
            Field(TitleBlockContentField.FirmName, "firm.name", "Firm name", 12, TitleBlockFieldStyle.Prominent),
            Field(TitleBlockContentField.FirmAddress, "firm.address", "Firm address", 14),
            Field(TitleBlockContentField.FirmPhone, "firm.phone", "Phone"),
            Field(TitleBlockContentField.FirmEmail, "firm.email", "Email"),
            Field(TitleBlockContentField.FirmWebsite, "firm.website", "Website"),
            Field(TitleBlockContentField.FirmRegistration, "firm.registration", "Registration / license"),
            Field(TitleBlockContentField.ProjectStatus, "project.status", "Status", 15, TitleBlockFieldStyle.Prominent),
            Field(TitleBlockContentField.ClientName, "project.client", "Client"),
            Field(TitleBlockContentField.SiteAddress, "project.site", "Site address", 14),
            Field(TitleBlockContentField.ProjectPhase, "project.phase", "Phase"),
        };
        foreach (var field in fields)
            if (field is not null) yield return field;
        foreach (var custom in options.CustomFields.Where(item => item.IsIncluded))
            yield return new FieldDescriptor(null, $"custom.{custom.Label}", custom.Label, 10);
        var trailing = new FieldDescriptor?[]
        {
            Field(TitleBlockContentField.IssueDate, "issue.date", "Issue date"),
            Field(TitleBlockContentField.IssuePurpose, "issue.purpose", "Issue purpose"),
            Field(TitleBlockContentField.DrawnBy, "issue.drawn_by", "Drawn by"),
            Field(TitleBlockContentField.CheckedBy, "issue.checked_by", "Checked by"),
            Field(TitleBlockContentField.ApprovedBy, "issue.approved_by", "Approved by"),
            Field(TitleBlockContentField.ProjectName, "project.name", "Project name", 18,
                TitleBlockFieldStyle.Prominent),
            Field(TitleBlockContentField.ProjectNumber, "project.number", "Project no."),
        };
        foreach (var field in trailing)
            if (field is not null) yield return field;
    }

    private static int RequiredColumns(
        IReadOnlyList<FieldDescriptor> fields,
        double availableHeight,
        int maximumColumns)
    {
        if (fields.Count == 0) return 1;
        for (var columns = 1; columns <= Math.Max(1, maximumColumns); columns++)
            if (RequiredColumnHeight(fields, columns) <= availableHeight + 0.001) return columns;
        throw new InvalidOperationException("The enabled title-block fields cannot fit legibly on this paper size.");
    }

    private static double RequiredColumnHeight(IReadOnlyList<FieldDescriptor> fields, int columns)
    {
        if (fields.Count == 0) return 0;
        var heights = new double[Math.Max(1, columns)];
        foreach (var field in fields) heights[Array.IndexOf(heights, heights.Min())] += field.Height;
        return heights.Max();
    }

    private static void PackColumns(
        ICollection<TitleBlockFieldPlacement> output,
        IReadOnlyList<FieldDescriptor> fields,
        double left,
        double bottom,
        double width,
        double height,
        int columnCount)
    {
        if (fields.Count == 0) return;
        var columns = Enumerable.Range(0, Math.Max(1, columnCount))
            .Select(_ => new List<FieldDescriptor>()).ToArray();
        var heights = new double[columns.Length];
        foreach (var field in fields)
        {
            var column = Array.IndexOf(heights, heights.Min());
            columns[column].Add(field);
            heights[column] += field.Height;
        }
        var columnWidth = width / columns.Length;
        for (var column = 0; column < columns.Length; column++)
        {
            var y = bottom + height;
            foreach (var field in columns[column])
            {
                y -= field.Height;
                output.Add(Placement(field, left + column * columnWidth, y, columnWidth, field.Height));
            }
        }
    }

    private static TitleBlockFieldPlacement Placement(
        FieldDescriptor field, double left, double bottom, double width, double height) =>
        new(field.Key, field.Label, new TitleBlockRectangle(left, bottom, width, height), field.Style);

    private static string Signature(
        BuiltInTitleBlockKind kind,
        double pageWidth,
        double pageHeight,
        TitleBlockRectangle block,
        ProjectInformation project,
        TitleBlockContentOptions options,
        bool includeScale)
    {
        var source = string.Join("|",
            string.Join(",", options.IncludedFields.Select(value => (int)value)),
            string.Join(";", options.CustomFields.Select(value => $"{value.Label}:{value.IsIncluded}")),
            options.ReserveRevisionArea.ToString(),
            includeScale.ToString(),
            project.Logo?.Sha256 ?? "no-logo");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12].ToLowerInvariant();
        return FormattableString.Invariant(
            $"tb{StyleVersion}:{kind}:{pageWidth:0.##}x{pageHeight:0.##}:{block.Width:0.##}x{block.Height:0.##}:{hash}");
    }

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

    private sealed record FieldDescriptor(
        TitleBlockContentField? Field,
        string Key,
        string Label,
        double Height,
        TitleBlockFieldStyle Style = TitleBlockFieldStyle.Standard);

    private sealed record Composition(
        TitleBlockRectangle Block,
        TitleBlockRectangle Content,
        IReadOnlyList<TitleBlockFieldPlacement> Fields,
        TitleBlockRectangle? Logo,
        TitleBlockRectangle? Revision);
}
