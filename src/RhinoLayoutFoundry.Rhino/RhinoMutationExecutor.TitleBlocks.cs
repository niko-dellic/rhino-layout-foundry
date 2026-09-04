using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed partial class RhinoMutationExecutor
{
    internal static Guid? CreateTitleBlock(
        RhinoDoc document,
        RhinoPageView page,
        TitleBlockTemplateRecipe titleBlock,
        PaperRecipe paper,
        ProjectInformation projectInfo,
        SheetTitleBlockData sheetData,
        IReadOnlyList<DetailSlotRecipe> details)
    {
        if (titleBlock.BuiltInKind is { } builtInKind)
            return CreateManagedTitleBlock(document, page, paper, builtInKind, projectInfo, sheetData, details);

        var definition = document.InstanceDefinitions.Find(titleBlock.InstanceDefinitionId, true)
            ?? document.InstanceDefinitions.Find(titleBlock.InstanceDefinitionName);
        if (definition is null)
            return null;
        var attributes = new ObjectAttributes
        {
            Space = ActiveSpace.PageSpace,
            ViewportId = page.MainViewport.Id,
        };
        var id = document.Objects.AddInstanceObject(definition.Index, RestoreTransform(titleBlock.Transform), attributes);
        if (id == Guid.Empty)
            throw new InvalidOperationException($"Rhino did not place title block '{titleBlock.InstanceDefinitionName}'.");
        return id;
    }

    private OperationResult ApplyProjectInformation(
        RhinoDoc document,
        OperationPlan plan,
        UpdateProjectInformationChange change)
    {
        var beforeState = _stateStore.Get(document);
        var undoRecord = document.BeginUndoRecord(plan.UndoDescription);
        if (undoRecord == 0)
            return Failure("operation.undo_unavailable", "Rhino could not start a project-information undo record.");

        var createdIds = new List<Guid>();
        var deletedObjects = new List<RhinoObject>();
        try
        {
            if (!document.AddCustomUndoEvent(
                    plan.UndoDescription,
                    OnUndoDocumentState,
                    new DocumentStateUndoTag(plan.UndoDescription, beforeState)))
                return Failure("operation.undo_unavailable", "Rhino could not register project information with Undo.");

            var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var pair in beforeState.Sheets)
            {
                var role = pair.Value.TitleBlock;
                if (role?.BuiltInKind is not { } kind) continue;
                var page = document.Views.GetPageViews()
                    .FirstOrDefault(candidate => candidate.MainViewport.Id == pair.Key);
                if (page is null || document.Objects.FindId(role.InstanceObjectId) is not InstanceObject oldInstance)
                    continue;

                var details = page.GetDetailViews()
                    .Select(detail => CaptureDetail(document, detail)).ToArray();
                var paper = new PaperRecipe(page.PageWidth, page.PageHeight, document.PageUnitSystem.ToString());
                var sheetData = pair.Value.TitleBlockData ?? new SheetTitleBlockData(string.Empty, []);
                var replacementId = CreateManagedTitleBlock(
                    document, page, paper, kind, change.NewInformation, sheetData, details);
                createdIds.Add(replacementId);
                var replacement = document.Objects.FindId(replacementId) as InstanceObject
                    ?? throw new InvalidOperationException("Rhino could not find the refreshed title block.");
                if (!document.Objects.Delete(oldInstance.Id, quiet: true))
                    throw new InvalidOperationException("Rhino could not replace an existing managed title block.");
                deletedObjects.Add(oldInstance);
                sheets[pair.Key] = pair.Value with
                {
                    TitleBlock = role with
                    {
                        InstanceObjectId = replacementId,
                        InstanceDefinitionId = replacement.InstanceDefinition.Id,
                        BuiltInKind = kind == BuiltInTitleBlockKind.FullWidthBottom
                            ? BuiltInTitleBlockKind.FullWidthBottom
                            : BuiltInTitleBlockKind.RightSidebar,
                    },
                };
            }

            _stateStore.SetCurrentSchema(document, beforeState with
            {
                ProjectData = change.NewInformation,
                Sheets = sheets,
            });
            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            DeleteUnusedGeneratedTitleBlockDefinitions(document);
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            foreach (var id in createdIds.AsEnumerable().Reverse())
                document.Objects.Delete(id, quiet: true);
            foreach (var item in deletedObjects.AsEnumerable().Reverse())
                document.Objects.Undelete(item);
            _stateStore.Set(document, beforeState);
            return Failure("project.apply_failed",
                $"Project information was not changed: {exception.Message}");
        }
        finally
        {
            document.EndUndoRecord(undoRecord);
        }
    }

    private static Guid CreateManagedTitleBlock(
        RhinoDoc document,
        RhinoPageView page,
        PaperRecipe paper,
        BuiltInTitleBlockKind kind,
        ProjectInformation projectInfo,
        SheetTitleBlockData sheetData,
        IReadOnlyList<DetailSlotRecipe> details)
    {
        var recipeUnit = ParseUnitSystem(paper.UnitSystem);
        var pageScale = RhinoMath.UnitScale(recipeUnit, document.PageUnitSystem);
        var layout = AdaptiveTitleBlockLayoutSolver.Solve(kind, paper, projectInfo, details.Count);
        var definitionName = $"RLF {layout.Signature.Replace(':', '-')}";
        var definition = document.InstanceDefinitions.Find(definitionName);
        if (definition is null)
        {
            var geometry = new List<GeometryBase>();
            var attributes = new List<ObjectAttributes>();
            var memberAttributes = new ObjectAttributes();
            void Add(GeometryBase item, ObjectAttributes? itemAttributes = null)
            {
                geometry.Add(item);
                attributes.Add(itemAttributes ?? memberAttributes.Duplicate());
            }

            double X(double value) => value * pageScale;
            var block = new TitleBlockRectangle(
                X(layout.Block.Left), X(layout.Block.Bottom), X(layout.Block.Width), X(layout.Block.Height));
            var margin = X(layout.Margin);
            AddRectangle(Add, margin, margin,
                X(paper.Width) - margin * 2, X(paper.Height) - margin * 2);
            AddRectangle(Add, block.Left, block.Bottom, block.Width, block.Height);

            var body = X(layout.BodyTextHeight);
            var heading = X(layout.HeadingTextHeight);
            foreach (var field in layout.Fields)
            {
                var bounds = new TitleBlockRectangle(
                    X(field.Bounds.Left), X(field.Bounds.Bottom), X(field.Bounds.Width), X(field.Bounds.Height));
                AddFieldCell(document, Add, field.Key, field.Label, bounds, body, heading, field.Style);
            }
            if (layout.RevisionRegion is { } revisionRegion)
            {
                var revision = new TitleBlockRectangle(
                    X(revisionRegion.Left), X(revisionRegion.Bottom),
                    X(revisionRegion.Width), X(revisionRegion.Height));
                AddRectangle(Add, revision.Left, revision.Bottom, revision.Width, revision.Height);
                AddPlainText(document, Add, "REVISIONS", revision.Left + body * 0.65,
                    revision.Top - body * 0.65, body * 0.72, revision.Width - body * 1.3);
            }

            Guid pictureId = Guid.Empty;
            try
            {
                if (projectInfo.Logo is { } logo && layout.LogoRegion is { } logoRegion)
                {
                    var logoPath = EnsureCachedLogoFile(logo);
                    using var logoImage = new Eto.Drawing.Bitmap(logo.Data);
                    var logoBounds = new TitleBlockRectangle(
                        X(logoRegion.Left), X(logoRegion.Bottom), X(logoRegion.Width), X(logoRegion.Height));
                    AddRectangle(Add, logoBounds.Left, logoBounds.Bottom, logoBounds.Width, logoBounds.Height);
                    var logoInset = Math.Max(body * 0.65, X(layout.Gutter) * 0.35);
                    var logoWidth = Math.Max(0, logoBounds.Width - logoInset * 2);
                    var logoHeight = Math.Max(0, logoBounds.Height - logoInset * 2);
                    var imageAspect = logoImage.Width / (double)Math.Max(1, logoImage.Height);
                    if (logoWidth / logoHeight > imageAspect)
                        logoWidth = logoHeight * imageAspect;
                    else
                        logoHeight = logoWidth / imageAspect;
                    var plane = new Plane(
                        new Point3d(
                            logoBounds.Left + (logoBounds.Width - logoWidth) / 2,
                            logoBounds.Bottom + (logoBounds.Height - logoHeight) / 2,
                            0),
                        Vector3d.XAxis,
                        Vector3d.YAxis);
                    pictureId = document.Objects.AddPictureFrame(
                        plane, logoPath, false, logoWidth, logoHeight, true, true);
                    if (pictureId != Guid.Empty && document.Objects.FindId(pictureId) is { } picture)
                    {
                        var pictureAttributes = picture.Attributes.Duplicate();
                        pictureAttributes.Space = ActiveSpace.ModelSpace;
                        pictureAttributes.ViewportId = Guid.Empty;
                        Add(picture.Geometry.Duplicate(), pictureAttributes);
                    }
                    else
                        throw new InvalidOperationException("Rhino could not create the embedded project logo.");
                }

                var index = document.InstanceDefinitions.Add(
                    definitionName,
                    $"Adaptive Layout Foundry title block ({AdaptiveTitleBlockLayoutSolver.Label(kind)})",
                    Point3d.Origin,
                    geometry,
                    attributes);
                if (index < 0)
                    throw new InvalidOperationException("Rhino could not create the adaptive title-block definition.");
                definition = document.InstanceDefinitions[index];
            }
            finally
            {
                if (pictureId != Guid.Empty) document.Objects.Delete(pictureId, quiet: true);
            }
        }

        var instanceAttributes = new ObjectAttributes
        {
            Space = ActiveSpace.PageSpace,
            ViewportId = page.MainViewport.Id,
        };
        foreach (var pair in TitleBlockValues(projectInfo, page.PageName, sheetData, details))
            SetBlockAttributeValue(instanceAttributes, pair.Key, pair.Value);
        var id = document.Objects.AddInstanceObject(definition.Index, Transform.Identity, instanceAttributes);
        if (id == Guid.Empty)
            throw new InvalidOperationException("Rhino did not place the adaptive title block.");
        return id;
    }

    private static void AddRectangle(
        Action<GeometryBase, ObjectAttributes?> add,
        double left,
        double bottom,
        double width,
        double height)
    {
        var polyline = new Polyline
        {
            new(left, bottom, 0),
            new(left + width, bottom, 0),
            new(left + width, bottom + height, 0),
            new(left, bottom + height, 0),
            new(left, bottom, 0),
        };
        add(new PolylineCurve(polyline), null);
    }

    private static void AddFieldCell(
        RhinoDoc document,
        Action<GeometryBase, ObjectAttributes?> add,
        string key,
        string prompt,
        TitleBlockRectangle bounds,
        double bodyHeight,
        double headingHeight,
        TitleBlockFieldStyle style)
    {
        AddRectangle(add, bounds.Left, bounds.Bottom, bounds.Width, bounds.Height);
        var inset = Math.Max(bodyHeight * 0.55, bounds.Height * 0.07);
        var safeWidth = Math.Max(bodyHeight * 2, bounds.Width - inset * 2);
        var labelHeight = Math.Min(bodyHeight * 0.62, bounds.Height * 0.20);
        var valueHeight = style switch
        {
            TitleBlockFieldStyle.SheetNumber => Math.Min(headingHeight * 1.45, bounds.Height * 0.48),
            TitleBlockFieldStyle.Prominent => Math.Min(headingHeight, bounds.Height * 0.40),
            _ => Math.Min(bodyHeight, bounds.Height * 0.34),
        };
        var x = bounds.Left + inset;
        var y = bounds.Top - inset;
        var labelPlane = new Plane(new Point3d(x, y, 0), Vector3d.XAxis, Vector3d.YAxis);
        var label = TextEntity.Create(prompt.ToUpperInvariant(), labelPlane, document.DimStyles.Current, false, safeWidth, 0);
        if (label is not null)
        {
            label.TextHeight = labelHeight;
            label.TextHorizontalAlignment = TextHorizontalAlignment.Left;
            label.TextVerticalAlignment = TextVerticalAlignment.Top;
            add(label, null);
        }

        var valueTop = y - labelHeight * 1.3;
        var valuePlane = new Plane(new Point3d(x, valueTop, 0), Vector3d.XAxis, Vector3d.YAxis);
        var escapedKey = EscapeTextFieldArgument(key);
        var escapedPrompt = EscapeTextFieldArgument(prompt);
        var field = $"%<UserText(\"block\",\"{escapedKey}\",\"{escapedPrompt}\",\"{EmptyTitleBlockValue}\")>%";
        var value = TextEntity.Create(field, valuePlane, document.DimStyles.Current, true, safeWidth, 0);
        if (value is null) return;
        value.TextHeight = valueHeight;
        value.TextHorizontalAlignment = TextHorizontalAlignment.Left;
        value.TextVerticalAlignment = TextVerticalAlignment.Top;
        add(value, null);
    }

    private static void AddPlainText(
        RhinoDoc document,
        Action<GeometryBase, ObjectAttributes?> add,
        string text,
        double x,
        double y,
        double height,
        double width)
    {
        var plane = new Plane(new Point3d(x, y, 0), Vector3d.XAxis, Vector3d.YAxis);
        var entity = TextEntity.Create(text, plane, document.DimStyles.Current, false,
            Math.Max(width, height * 2), 0);
        if (entity is null) return;
        entity.TextHeight = height;
        entity.TextHorizontalAlignment = TextHorizontalAlignment.Left;
        entity.TextVerticalAlignment = TextVerticalAlignment.Top;
        add(entity, null);
    }

    private static void SetBlockAttributeValue(ObjectAttributes attributes, string key, string value)
    {
        attributes.SetUserString(key, string.IsNullOrEmpty(value) ? EmptyTitleBlockValue : value);
    }

    private const string EmptyTitleBlockValue = "\u00A0";

    private static string EnsureCachedLogoFile(BrandAsset logo)
    {
        var extension = logo.MediaType == "image/png" ? ".png" : ".jpg";
        var directory = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Rhino Layout Foundry",
            "Logos");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"v{AdaptiveTitleBlockLayoutSolver.StyleVersion}-{logo.Sha256}{extension}");
        if (!File.Exists(path) || new FileInfo(path).Length != logo.Data.Length)
            File.WriteAllBytes(path, logo.Data);
        return path;
    }

    private static string EscapeTextFieldArgument(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void DeleteUnusedGeneratedTitleBlockDefinitions(RhinoDoc document)
    {
        for (var index = 0; index < document.InstanceDefinitions.Count; index++)
        {
            var definition = document.InstanceDefinitions[index];
            if (definition is null || definition.IsDeleted ||
                !definition.Name.StartsWith("RLF tb", StringComparison.Ordinal) ||
                definition.UseCount() != 0)
                continue;
            document.InstanceDefinitions.Delete(index, deleteReferences: false, quiet: true);
        }
    }

    private static IReadOnlyDictionary<string, string> TitleBlockValues(
        ProjectInformation project,
        string sheetTitle,
        SheetTitleBlockData sheet,
        IReadOnlyList<DetailSlotRecipe> details)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["project.name"] = project.ProjectName,
            ["project.number"] = project.ProjectNumber,
            ["project.client"] = project.ClientName,
            ["project.site"] = project.SiteAddress,
            ["project.phase"] = project.ProjectPhase,
            ["project.status"] = project.ProjectStatus,
            ["firm.name"] = project.FirmName,
            ["firm.address"] = project.FirmAddress,
            ["firm.phone"] = project.FirmPhone,
            ["firm.email"] = project.FirmEmail,
            ["firm.website"] = project.FirmWebsite,
            ["firm.registration"] = project.FirmRegistration,
            ["issue.date"] = project.IssueDate,
            ["issue.purpose"] = project.IssuePurpose,
            ["issue.drawn_by"] = project.DrawnBy,
            ["issue.checked_by"] = project.CheckedBy,
            ["issue.approved_by"] = project.ApprovedBy,
            ["sheet.number"] = sheet.SheetNumber,
            ["sheet.title"] = sheetTitle,
        };
        if (details.Count == 1)
            result["sheet.scale"] = ScaleSummary(details);
        for (var index = 1; index <= 6; index++)
            result[$"revision.{index}.summary"] = string.Empty;
        foreach (var pair in project.CustomFields) result[$"custom.{pair.Key}"] = pair.Value;
        foreach (var pair in sheet.Custom) result[$"sheet.custom.{pair.Key}"] = pair.Value;
        for (var index = 0; index < sheet.Revisions.Count; index++)
        {
            var revision = sheet.Revisions[index];
            result[$"revision.{index + 1}.summary"] = string.Join(" · ", new[]
            {
                revision.Code, revision.Date, revision.Description, revision.IssuedBy, revision.CheckedBy,
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        return result;
    }

    private static string ScaleSummary(IReadOnlyList<DetailSlotRecipe> details)
    {
        if (details.Count == 0) return "N/A";
        var ratios = details.Where(detail => detail.PageToModelRatio is > 0)
            .Select(detail => detail.PageToModelRatio!.Value)
            .DistinctBy(value => Math.Round(value, 8))
            .ToArray();
        if (ratios.Length != 1) return "As indicated";
        var denominator = 1 / ratios[0];
        return $"1:{denominator:0.##}";
    }

}
