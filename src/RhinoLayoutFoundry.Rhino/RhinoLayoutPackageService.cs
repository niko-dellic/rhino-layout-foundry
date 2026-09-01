using System.Security.Cryptography;
using System.Text;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoLayoutPackageService : ILayoutPackageService
{
    private static readonly HashSet<Guid> BuiltInDisplayModeIds =
    [
        DisplayModeDescription.ArtisticId,
        DisplayModeDescription.GhostedId,
        DisplayModeDescription.PenId,
        DisplayModeDescription.RenderedId,
        DisplayModeDescription.RenderedShadowsId,
        DisplayModeDescription.ShadedId,
        DisplayModeDescription.TechId,
        DisplayModeDescription.WireframeId,
        DisplayModeDescription.XRayId,
        DisplayModeDescription.AmbientOcclusionId,
        DisplayModeDescription.RaytracedId,
        DisplayModeDescription.MonochromeId,
    ];

    private readonly DocumentStateStore _stateStore;
    private readonly DocumentRevisionTracker _revisionTracker;
    private readonly Action _changed;

    public RhinoLayoutPackageService(
        DocumentStateStore stateStore,
        DocumentRevisionTracker revisionTracker,
        Action changed)
    {
        _stateStore = stateStore;
        _revisionTracker = revisionTracker;
        _changed = changed;
    }

    public Task<LayoutPackageExportResult> ExportAsync(
        LayoutPackageExportRequest request,
        CancellationToken cancellationToken = default) =>
        RunOnUiThread(() => ExportOnUiThread(request, cancellationToken));

    public Task<LayoutPackagePreflight> PreflightAsync(
        string filePath,
        CancellationToken cancellationToken = default) =>
        RunOnUiThread(() => PreflightOnUiThread(filePath, cancellationToken));

    public Task<LayoutPackageImportResult> ImportAsync(
        LayoutPackageImportRequest request,
        CancellationToken cancellationToken = default) =>
        RunOnUiThread(() => ImportOnUiThread(request, cancellationToken, createRecovery: true));

    private LayoutPackageExportResult ExportOnUiThread(
        LayoutPackageExportRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = RequireDocument(request.DocumentRuntimeSerialNumber, request.SourceRevision);
            var package = CapturePackage(document, cancellationToken);
            LayoutPackageArchive.Write(request.FilePath, package.Manifest, package.Assets);
            return new LayoutPackageExportResult(true, package.Manifest.Sheets.Count);
        }
        catch (OperationCanceledException)
        {
            return new LayoutPackageExportResult(false, 0, "Layout package export was cancelled.");
        }
        catch (Exception exception)
        {
            return new LayoutPackageExportResult(false, 0,
                $"Layout package export failed: {exception.Message}");
        }
    }

    private LayoutPackagePreflight PreflightOnUiThread(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contents = LayoutPackageArchive.Read(filePath);
            var document = RhinoDoc.ActiveDoc;
            var conflicts = document is null
                ? []
                : FindConflicts(document, contents).ToArray();
            var warnings = new List<string>();
            if (document is null)
                warnings.Add("Open a Rhino document before importing this package.");
            if (contents.Manifest.FoundryState.Recovery.Count > 0)
                warnings.Add($"The package contains {contents.Manifest.FoundryState.Recovery.Count} unresolved recovery record(s).");
            if (document is not null)
                warnings.AddRange(FindMissingLayerWarnings(document, contents));
            return new LayoutPackagePreflight(true, filePath, contents.Manifest, conflicts, warnings);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or NotSupportedException or
            System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return new LayoutPackagePreflight(false, filePath, null, [], [], exception.Message);
        }
    }

    private LayoutPackageImportResult ImportOnUiThread(
        LayoutPackageImportRequest request,
        CancellationToken cancellationToken,
        bool createRecovery)
    {
        string? recoveryPath = null;
        var replaceCutoverStarted = false;
        var createdPages = new List<RhinoPageView>();
        var importedDisplayModes = new List<Guid>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = RequireDocument(request.DocumentRuntimeSerialNumber, request.SourceRevision);
            var contents = LayoutPackageArchive.Read(request.FilePath);
            if (createRecovery && request.Mode == LayoutPackageImportMode.Replace &&
                document.Views.GetPageViews().Length > 0)
            {
                recoveryPath = Path.Combine(
                    Path.GetTempPath(),
                    $"LayoutFoundry-Recovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.rlf");
                var recovery = CapturePackage(document, cancellationToken);
                LayoutPackageArchive.Write(recoveryPath, recovery.Manifest, recovery.Assets);
            }

            var warnings = new List<string>();
            var resolutions = request.ConflictResolutions ??
                new Dictionary<string, LayoutPackageConflictResolution>(StringComparer.Ordinal);
            var displayModeMap = ImportDisplayModes(contents, resolutions, importedDisplayModes, warnings);
            var namedViewMap = ImportNamedViews(document, contents, resolutions, warnings);
            ImportNamedLayerStates(document, contents, resolutions, warnings);

            var beforeState = _stateStore.Get(document);
            var folderMap = BuildFolderMap(document, beforeState, contents.Manifest, request.Mode);
            var pagesBySource = new Dictionary<Guid, RhinoPageView>();
            var detailsBySource = new Dictionary<Guid, Guid>();
            var objectMap = new Dictionary<Guid, Guid>();
            var definitionMap = new Dictionary<Guid, InstanceDefinition>();
            var existingNames = document.Views.GetPageViews().Select(page => page.PageName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var sheet in contents.Manifest.Sheets.OrderBy(item => item.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var intendedName = UniqueName(sheet.Name, existingNames);
                var stageName = request.Mode == LayoutPackageImportMode.Replace
                    ? UniqueName($"__FoundryImport__{sheet.Name}", existingNames)
                    : intendedName;
                var unit = ParseUnitSystem(sheet.Paper.UnitSystem);
                var scale = RhinoMath.UnitScale(unit, document.PageUnitSystem);
                var page = document.Views.AddPageView(
                    stageName,
                    sheet.Paper.Width * scale,
                    sheet.Paper.Height * scale)
                    ?? throw new InvalidOperationException($"Rhino did not create layout '{sheet.Name}'.");
                createdPages.Add(page);
                pagesBySource.Add(sheet.SourcePageViewId, page);

                foreach (var detail in sheet.Details)
                {
                    var recipe = RemapDetailRecipe(
                        document,
                        detail.Recipe,
                        displayModeMap,
                        objectMap);
                    var created = CreateDetail(document, page, recipe, unit, scale);
                    detailsBySource[detail.SourceDetailViewportId] = created.Viewport.Id;
                    ApplyLayerOverrides(document, created.Viewport.Id, detail.LayerOverrides, warnings);
                    ApplyLayerRules(document, created.Viewport.Id, recipe.Layers, warnings);
                    ApplyObjectDisplayRules(document, created.Viewport.Id, recipe.Objects, warnings);
                }
            }

            ImportPageSpaceObjects(
                document, contents, pagesBySource, objectMap, definitionMap, resolutions, warnings);

            if (request.Mode == LayoutPackageImportMode.Replace)
            {
                replaceCutoverStarted = true;
                var importedIds = createdPages.Select(page => page.MainViewport.Id).ToHashSet();
                foreach (var oldPage in document.Views.GetPageViews()
                             .Where(page => !importedIds.Contains(page.MainViewport.Id)).ToArray())
                {
                    if (!oldPage.Close())
                        throw new InvalidOperationException($"Rhino could not remove layout '{oldPage.PageName}'.");
                }
                existingNames.Clear();
                foreach (var sheet in contents.Manifest.Sheets.OrderBy(item => item.Order))
                {
                    var page = pagesBySource[sheet.SourcePageViewId];
                    page.PageName = UniqueName(sheet.Name, existingNames);
                }
            }

            var importedState = RemapState(
                document,
                beforeState,
                contents.Manifest,
                request.Mode,
                request.ImportProjectInformation,
                folderMap,
                pagesBySource,
                detailsBySource,
                objectMap,
                definitionMap,
                displayModeMap,
                namedViewMap,
                resolutions,
                warnings);
            _stateStore.SetCurrentSchema(document, importedState);
            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            _changed();
            return new LayoutPackageImportResult(true, createdPages.Count, warnings, RecoveryPackagePath: recoveryPath);
        }
        catch (OperationCanceledException)
        {
            RemoveCreatedPages(createdPages);
            RemoveImportedDisplayModes(importedDisplayModes);
            return new LayoutPackageImportResult(false, 0, [], "Layout package import was cancelled.", recoveryPath);
        }
        catch (Exception exception)
        {
            RemoveCreatedPages(createdPages);
            RemoveImportedDisplayModes(importedDisplayModes);
            if (createRecovery && replaceCutoverStarted && recoveryPath is not null && File.Exists(recoveryPath) &&
                RhinoDoc.ActiveDoc is { } document)
            {
                var restoration = ImportOnUiThread(
                    new LayoutPackageImportRequest(
                        document.RuntimeSerialNumber,
                        _revisionTracker.Current(document),
                        recoveryPath,
                        LayoutPackageImportMode.Replace,
                        ImportProjectInformation: true),
                    CancellationToken.None,
                    createRecovery: false);
                var restorationMessage = restoration.Succeeded
                    ? " The original layouts were restored from the recovery package."
                    : $" Automatic restoration also failed: {restoration.ErrorMessage}";
                return new LayoutPackageImportResult(false, 0, [],
                    $"Layout package import failed: {exception.Message}{restorationMessage}", recoveryPath);
            }
            return new LayoutPackageImportResult(false, 0, [],
                $"Layout package import failed: {exception.Message}", recoveryPath);
        }
    }

    private CapturedPackage CapturePackage(RhinoDoc document, CancellationToken cancellationToken)
    {
        var state = WithCurrentPageRecords(document, _stateStore.Get(document));
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"LayoutFoundry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var assetPath = Path.Combine(temporaryDirectory, "layouts.3dm");
            using (var writeOptions = new FileWriteOptions
            {
                UpdateDocumentPath = false,
                IncludeRenderMeshes = false,
                IncludePreviewImage = false,
                IncludeHistory = false,
                SuppressDialogBoxes = true,
                SuppressAllInput = true,
                WriteUserData = true,
            })
            {
                if (!document.Write3dmFile(assetPath, writeOptions))
                    throw new IOException("Rhino could not create the package layout asset.");
            }
            StripModelGeometry(assetPath, document);

            var sheets = new List<LayoutPackageSheet>();
            var referencedDisplayModes = new HashSet<Guid>();
            foreach (var page in document.Views.GetPageViews().OrderBy(page => page.PageNumber))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageId = page.MainViewport.Id;
                var record = state.Sheets.GetValueOrDefault(pageId) ??
                    new SheetRecord(pageId, state.RootFolderId, page.PageNumber, [],
                        new Dictionary<string, string>(StringComparer.Ordinal), null);
                var details = page.GetDetailViews().Select(detail =>
                {
                    referencedDisplayModes.Add(detail.Viewport.DisplayMode.Id);
                    return new LayoutPackageDetail(
                        detail.Viewport.Id,
                        CaptureDetail(document, detail),
                        CaptureLayerOverrides(document, detail.Viewport.Id));
                }).ToArray();
                var pageObjectIds = document.Objects
                    .Where(item => item.Attributes.Space == ActiveSpace.PageSpace &&
                                   item.Attributes.ViewportId == pageId &&
                                   item is not DetailViewObject)
                    .Select(item => item.Id)
                    .ToArray();
                sheets.Add(new LayoutPackageSheet(
                    pageId,
                    record.FolderId,
                    record.Order,
                    page.PageName,
                    new PaperRecipe(page.PageWidth, page.PageHeight, document.PageUnitSystem.ToString()),
                    details,
                    pageObjectIds,
                    record.Tags,
                    record.Metadata,
                    record.TitleBlock,
                    record.IncludeInPrintAll,
                    record.TitleBlockData,
                    record.NamingBinding is { } binding && string.Equals(
                        page.PageName,
                        binding.LastGeneratedName,
                        StringComparison.Ordinal)
                        ? binding
                        : null,
                    record.Notes ?? string.Empty));
            }

            foreach (var rule in state.DisplayRules) referencedDisplayModes.Add(rule.DisplayModeId);
            foreach (var modeId in state.Templates.SelectMany(template => template.DetailSlots)
                         .Where(slot => slot.DisplayModeId is not null)
                         .Select(slot => slot.DisplayModeId!.Value))
                referencedDisplayModes.Add(modeId);

            var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [LayoutPackageManifest.LayoutAssetEntryName] = File.ReadAllBytes(assetPath),
            };
            var displayModes = CaptureDisplayModes(referencedDisplayModes, temporaryDirectory, assets);
            using var asset = File3dm.Read(assetPath)
                ?? throw new InvalidDataException("Rhino could not reopen the package layout asset.");
            var namedViews = asset.NamedViews.Select(CaptureNamedView).ToArray();
            var namedLayerStates = document.NamedLayerStates.Names
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new LayoutPackageNamedLayerState(name, Fingerprint(name)))
                .ToArray();
            var retainedObjectIds = asset.Objects.Select(item => item.Id).ToHashSet();
            var titleBlocks = asset.AllInstanceDefinitions
                .Where(definition => definition.GetObjectIds().Any(retainedObjectIds.Contains))
                .Select(definition => new LayoutPackageTitleBlockDefinition(
                    definition.Id,
                    definition.Name,
                    FingerprintDefinition(asset, definition)))
                .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var sourceName = string.IsNullOrWhiteSpace(document.Name)
                ? "Untitled"
                : Path.GetFileNameWithoutExtension(document.Name);
            var manifest = new LayoutPackageManifest(
                LayoutPackageManifest.CurrentPackageVersion,
                sourceName,
                DateTimeOffset.UtcNow,
                typeof(RhinoLayoutPackageService).Assembly.GetName().Version?.ToString() ?? "0.1.0",
                state with { SchemaVersion = DocumentState.CurrentSchemaVersion },
                sheets,
                namedViews,
                namedLayerStates,
                displayModes,
                new Dictionary<string, string>(StringComparer.Ordinal),
                titleBlocks);
            return new CapturedPackage(manifest, assets);
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void StripModelGeometry(string assetPath, RhinoDoc document)
    {
        using var file = File3dm.Read(assetPath)
            ?? throw new InvalidDataException("Rhino could not read the package layout asset.");
        var pageObjectIds = document.Objects
            .Where(item => item.Attributes.Space == ActiveSpace.PageSpace && item is not DetailViewObject)
            .Select(item => item.Id)
            .ToHashSet();
        var retainedIds = new HashSet<Guid>(pageObjectIds);
        var pendingDefinitions = new Queue<Guid>(file.Objects
            .Where(item => pageObjectIds.Contains(item.Id))
            .Select(item => item.Geometry)
            .OfType<InstanceReferenceGeometry>()
            .Select(reference => reference.ParentIdefId));
        var visitedDefinitions = new HashSet<Guid>();
        while (pendingDefinitions.TryDequeue(out var definitionId))
        {
            if (!visitedDefinitions.Add(definitionId)) continue;
            var definition = file.AllInstanceDefinitions.FirstOrDefault(item => item.Id == definitionId);
            if (definition is null) continue;
            foreach (var objectId in definition.GetObjectIds())
            {
                retainedIds.Add(objectId);
                var member = file.Objects.FirstOrDefault(item => item.Id == objectId);
                if (member?.Geometry is InstanceReferenceGeometry nested)
                    pendingDefinitions.Enqueue(nested.ParentIdefId);
            }
        }

        foreach (var item in file.Objects.Where(item => !retainedIds.Contains(item.Id)).ToArray())
            file.Objects.Delete(item.Id);
        file.PlugInData.Clear();
        if (!file.Write(assetPath, 8))
            throw new IOException("Rhino could not finalize the package layout asset.");
    }

    private static IReadOnlyList<LayoutPackageDisplayMode> CaptureDisplayModes(
        IReadOnlySet<Guid> referencedIds,
        string temporaryDirectory,
        IDictionary<string, byte[]> assets)
    {
        var result = new List<LayoutPackageDisplayMode>();
        foreach (var id in referencedIds.Order())
        {
            using var mode = DisplayModeDescription.GetDisplayMode(id);
            if (mode is null) continue;
            if (BuiltInDisplayModeIds.Contains(id))
            {
                result.Add(new LayoutPackageDisplayMode(id, mode.LocalName, id.ToString("N"), true, null));
                continue;
            }

            var safeName = $"{id:N}.ini";
            var path = Path.Combine(temporaryDirectory, safeName);
            if (!DisplayModeDescription.ExportToFile(mode, path) || !File.Exists(path)) continue;
            var bytes = File.ReadAllBytes(path);
            var entryName = $"display-modes/{safeName}";
            assets[entryName] = bytes;
            result.Add(new LayoutPackageDisplayMode(
                id,
                mode.LocalName,
                LayoutPackageArchive.Sha256(bytes),
                false,
                entryName));
        }
        return result;
    }

    private static LayoutPackageNamedView CaptureNamedView(ViewInfo view)
    {
        var viewport = view.Viewport;
        var values = new[]
        {
            viewport.CameraLocation.X, viewport.CameraLocation.Y, viewport.CameraLocation.Z,
            viewport.TargetPoint.X, viewport.TargetPoint.Y, viewport.TargetPoint.Z,
            viewport.CameraUp.X, viewport.CameraUp.Y, viewport.CameraUp.Z,
        };
        return new LayoutPackageNamedView(
            view.Name,
            Fingerprint(string.Join("|", values.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) +
                        $"|{viewport.IsPerspectiveProjection}"),
            values[..3], values[3..6], values[6..9], viewport.IsPerspectiveProjection);
    }

    private static DetailSlotRecipe CaptureDetail(RhinoDoc document, DetailViewObject detail)
    {
        var bounds = detail.DetailGeometry.GetBoundingBox(true);
        var viewport = detail.Viewport;
        return new DetailSlotRecipe(
            detail.Viewport.Id,
            string.IsNullOrWhiteSpace(detail.Attributes.Name) ? viewport.Name : detail.Attributes.Name,
            bounds.Min.X,
            bounds.Min.Y,
            bounds.Max.X,
            bounds.Max.Y,
            viewport.IsPerspectiveProjection ? "Perspective" : "Top",
            detail.DetailGeometry.IsParallelProjection ? detail.DetailGeometry.PageToModelRatio : null,
            detail.DetailGeometry.IsProjectionLocked,
            viewport.DisplayMode.Id,
            null,
            [viewport.CameraLocation.X, viewport.CameraLocation.Y, viewport.CameraLocation.Z],
            [viewport.CameraTarget.X, viewport.CameraTarget.Y, viewport.CameraTarget.Z],
            [viewport.CameraUp.X, viewport.CameraUp.Y, viewport.CameraUp.Z],
            document.Layers
                .Where(layer => !layer.IsDeleted && !layer.IsReference &&
                                layer.HasPerViewportSettings(viewport.Id))
                .Select(layer => new LayerVisibilityRule(
                    new LayerReference(layer.Id, layer.FullPath),
                    layer.PerViewportIsVisible(viewport.Id)
                        ? LayerVisibilityOverride.Visible
                        : LayerVisibilityOverride.Hidden))
                .ToArray(),
            document.Objects
                .Where(item => item is not DetailViewObject &&
                               item.Attributes.Space == ActiveSpace.ModelSpace &&
                               item.Attributes.HasDisplayModeOverride(viewport.Id))
                .Select(item =>
                {
                    var modeId = item.Attributes.GetDisplayModeOverride(viewport.Id);
                    using var mode = DisplayModeDescription.GetDisplayMode(modeId);
                    return new ObjectDisplayRule(
                        new ObjectDisplaySelector(ObjectDisplaySelectorKind.ExactObject, ObjectId: item.Id),
                        modeId,
                        mode?.LocalName ?? "Missing display mode");
                })
                .ToArray());
    }

    private static IReadOnlyList<LayoutPackageLayerOverride> CaptureLayerOverrides(
        RhinoDoc document,
        Guid detailViewportId) => document.Layers
        .Where(layer => layer.HasPerViewportSettings(detailViewportId))
        .OrderBy(layer => layer.FullPath, StringComparer.OrdinalIgnoreCase)
        .Select(layer => new LayoutPackageLayerOverride(
            layer.FullPath,
            layer.PerViewportIsVisible(detailViewportId)))
        .ToArray();

    private IEnumerable<LayoutPackageConflict> FindConflicts(
        RhinoDoc document,
        LayoutPackageContents contents)
    {
        foreach (var mode in contents.Manifest.DisplayModes.Where(item => !item.IsBuiltIn))
        {
            using var existing = DisplayModeDescription.FindByName(mode.Name);
            if (existing is null) continue;
            var existingFingerprint = ExportDisplayModeFingerprint(existing);
            if (string.Equals(existingFingerprint, mode.Fingerprint, StringComparison.OrdinalIgnoreCase)) continue;
            yield return Conflict(LayoutPackageDependencyKind.DisplayMode, mode.Name, canOverwrite: false);
        }

        foreach (var view in contents.Manifest.NamedViews)
        {
            var index = document.NamedViews.FindByName(view.Name);
            if (index < 0) continue;
            var existing = CaptureNamedView(document.NamedViews[index]);
            if (string.Equals(existing.Fingerprint, view.Fingerprint, StringComparison.OrdinalIgnoreCase)) continue;
            yield return Conflict(LayoutPackageDependencyKind.NamedView, view.Name, canOverwrite: true);
        }

        foreach (var state in contents.Manifest.NamedLayerStates)
            if (document.NamedLayerStates.FindName(state.Name) >= 0)
                yield return Conflict(LayoutPackageDependencyKind.NamedLayerState, state.Name, canOverwrite: true);

        foreach (var definition in contents.Manifest.TitleBlocks)
        {
            var existing = document.InstanceDefinitions.Find(definition.Name);
            if (existing is null ||
                string.Equals(FingerprintDefinition(existing), definition.Fingerprint,
                    StringComparison.OrdinalIgnoreCase)) continue;
            yield return Conflict(LayoutPackageDependencyKind.TitleBlockDefinition, definition.Name, canOverwrite: false);
        }

        foreach (var template in contents.Manifest.FoundryState.Templates)
            if (_stateStore.Get(document).Templates.Any(existing =>
                    string.Equals(existing.Name, template.Name, StringComparison.OrdinalIgnoreCase) &&
                    existing != template))
                yield return Conflict(LayoutPackageDependencyKind.Template, template.Name, canOverwrite: false);
    }

    private static LayoutPackageConflict Conflict(
        LayoutPackageDependencyKind kind,
        string name,
        bool canOverwrite) => new(
            $"{kind}:{name}",
            kind,
            name,
            $"A different {kind.ToString().ToLowerInvariant()} named '{name}' already exists.",
            LayoutPackageConflictResolution.ImportRenamedCopy,
            canOverwrite);

    private static IEnumerable<string> FindMissingLayerWarnings(
        RhinoDoc document,
        LayoutPackageContents contents) => contents.Manifest.Sheets
        .SelectMany(sheet => sheet.Details)
        .SelectMany(detail => detail.LayerOverrides)
        .Select(item => item.LayerFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(path => document.Layers.FindByFullPath(path, -1) < 0)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .Select(path => $"Layer '{path}' is unavailable; its detail state will be retained as unresolved.");

    private static Dictionary<Guid, Guid> ImportDisplayModes(
        LayoutPackageContents contents,
        IReadOnlyDictionary<string, LayoutPackageConflictResolution> resolutions,
        ICollection<Guid> importedIds,
        ICollection<string> warnings)
    {
        var result = new Dictionary<Guid, Guid>();
        foreach (var item in contents.Manifest.DisplayModes)
        {
            if (item.IsBuiltIn)
            {
                using var byId = DisplayModeDescription.GetDisplayMode(item.SourceId);
                using var byName = byId is null ? DisplayModeDescription.FindByName(item.Name) : null;
                var resolved = byId?.Id ?? byName?.Id ?? Guid.Empty;
                if (resolved != Guid.Empty) result[item.SourceId] = resolved;
                else warnings.Add($"Built-in display mode '{item.Name}' is unavailable.");
                continue;
            }

            using var existing = DisplayModeDescription.FindByName(item.Name);
            var key = $"{LayoutPackageDependencyKind.DisplayMode}:{item.Name}";
            var resolution = resolutions.GetValueOrDefault(key, LayoutPackageConflictResolution.ImportRenamedCopy);
            if (existing is not null &&
                (string.Equals(ExportDisplayModeFingerprint(existing), item.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
                 resolution == LayoutPackageConflictResolution.ReuseDestination))
            {
                result[item.SourceId] = existing.Id;
                continue;
            }

            if (item.AssetPath is null || !contents.Assets.TryGetValue(item.AssetPath, out var bytes))
            {
                warnings.Add($"Custom display mode '{item.Name}' has no portable definition.");
                continue;
            }
            var tempPath = Path.Combine(Path.GetTempPath(), $"LayoutFoundry-{Guid.NewGuid():N}.ini");
            try
            {
                File.WriteAllBytes(tempPath, bytes);
                var importedId = DisplayModeDescription.ImportFromFile(tempPath, false);
                if (importedId == Guid.Empty)
                {
                    warnings.Add($"Custom display mode '{item.Name}' could not be imported.");
                    continue;
                }
                importedIds.Add(importedId);
                result[item.SourceId] = importedId;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> ImportNamedViews(
        RhinoDoc document,
        LayoutPackageContents contents,
        IReadOnlyDictionary<string, LayoutPackageConflictResolution> resolutions,
        ICollection<string> warnings)
    {
        var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assetBytes = contents.Assets[LayoutPackageManifest.LayoutAssetEntryName];
        using var file = File3dm.FromByteArray(assetBytes);
        if (file is null) return nameMap;
        foreach (var packaged in contents.Manifest.NamedViews)
        {
            var source = file.NamedViews.FirstOrDefault(view =>
                string.Equals(view.Name, packaged.Name, StringComparison.OrdinalIgnoreCase));
            if (source is null) continue;
            var existingIndex = document.NamedViews.FindByName(packaged.Name);
            if (existingIndex >= 0)
            {
                var existing = CaptureNamedView(document.NamedViews[existingIndex]);
                if (existing.Fingerprint == packaged.Fingerprint)
                {
                    nameMap[packaged.Name] = packaged.Name;
                    continue;
                }
                var key = $"{LayoutPackageDependencyKind.NamedView}:{packaged.Name}";
                var resolution = resolutions.GetValueOrDefault(key, LayoutPackageConflictResolution.ImportRenamedCopy);
                if (resolution == LayoutPackageConflictResolution.ReuseDestination)
                {
                    nameMap[packaged.Name] = packaged.Name;
                    continue;
                }
                if (resolution == LayoutPackageConflictResolution.ImportRenamedCopy)
                    source.Name = UniqueName(packaged.Name, document.NamedViews.Select(view => view.Name));
            }
            if (document.NamedViews.Add(source) < 0)
                warnings.Add($"Named view '{packaged.Name}' could not be imported.");
            else nameMap[packaged.Name] = source.Name;
        }
        return nameMap;
    }

    private static void ImportNamedLayerStates(
        RhinoDoc document,
        LayoutPackageContents contents,
        IReadOnlyDictionary<string, LayoutPackageConflictResolution> resolutions,
        ICollection<string> warnings)
    {
        if (contents.Manifest.NamedLayerStates.Count == 0) return;
        var tempPath = Path.Combine(Path.GetTempPath(), $"LayoutFoundry-{Guid.NewGuid():N}.3dm");
        var stagedPath = $"{tempPath}.staged.3dm";
        try
        {
            File.WriteAllBytes(tempPath, contents.Assets[LayoutPackageManifest.LayoutAssetEntryName]);
            using (var headless = RhinoDoc.OpenHeadless(tempPath))
            {
                if (headless is not null)
                {
                    var destinationNames = document.NamedLayerStates.Names.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var state in contents.Manifest.NamedLayerStates)
                    {
                        if (!destinationNames.Contains(state.Name)) continue;
                        var key = $"{LayoutPackageDependencyKind.NamedLayerState}:{state.Name}";
                        var resolution = resolutions.GetValueOrDefault(
                            key, LayoutPackageConflictResolution.ImportRenamedCopy);
                        if (resolution == LayoutPackageConflictResolution.ReuseDestination)
                            headless.NamedLayerStates.Delete(state.Name);
                        else if (resolution == LayoutPackageConflictResolution.ImportRenamedCopy)
                            headless.NamedLayerStates.Rename(
                                state.Name,
                                UniqueName(state.Name, destinationNames));
                    }
                    using var options = new FileWriteOptions
                    {
                        UpdateDocumentPath = false,
                        SuppressDialogBoxes = true,
                        SuppressAllInput = true,
                        WriteUserData = true,
                    };
                    if (!headless.Write3dmFile(stagedPath, options))
                        warnings.Add("Named layer-state conflict choices could not be staged.");
                }
            }
            var importPath = File.Exists(stagedPath) ? stagedPath : tempPath;
            if (document.NamedLayerStates.Import(importPath) <= 0)
                warnings.Add("One or more named layer states could not be imported.");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(stagedPath)) File.Delete(stagedPath);
        }
    }

    private static Dictionary<Guid, Guid> BuildFolderMap(
        RhinoDoc document,
        DocumentState before,
        LayoutPackageManifest manifest,
        LayoutPackageImportMode mode)
    {
        var map = new Dictionary<Guid, Guid>();
        if (mode == LayoutPackageImportMode.Replace)
        {
            map[manifest.FoundryState.RootFolderId] = before.RootFolderId;
            foreach (var folder in manifest.FoundryState.Folders.Where(folder =>
                         folder.Id != manifest.FoundryState.RootFolderId))
                map[folder.Id] = Guid.NewGuid();
            return map;
        }

        map[manifest.FoundryState.RootFolderId] = Guid.NewGuid();
        foreach (var folder in manifest.FoundryState.Folders.Where(folder =>
                     folder.Id != manifest.FoundryState.RootFolderId))
            map[folder.Id] = Guid.NewGuid();
        return map;
    }

    private static DetailViewObject CreateDetail(
        RhinoDoc document,
        RhinoPageView page,
        DetailSlotRecipe recipe,
        UnitSystem recipeUnit,
        double pageScale)
    {
        var projection = Enum.TryParse<DefinedViewportProjection>(recipe.Projection, true, out var parsed)
            ? parsed
            : DefinedViewportProjection.Top;
        var detail = page.AddDetailView(
            recipe.Name,
            new Point2d(recipe.Left * pageScale, recipe.Bottom * pageScale),
            new Point2d(recipe.Right * pageScale, recipe.Top * pageScale),
            projection) ?? throw new InvalidOperationException($"Rhino did not create detail '{recipe.Name}'.");
        var geometryChanged = false;
        if (recipe.PageToModelRatio is { } ratio && ratio > 0 && detail.DetailGeometry.IsParallelProjection)
        {
            if (!detail.DetailGeometry.SetScale(ratio, recipeUnit, 1, document.ModelUnitSystem))
                throw new InvalidOperationException($"Rhino did not set the scale for detail '{recipe.Name}'.");
            geometryChanged = true;
        }
        if (detail.DetailGeometry.IsProjectionLocked != recipe.ProjectionLocked)
        {
            detail.DetailGeometry.IsProjectionLocked = recipe.ProjectionLocked;
            geometryChanged = true;
        }
        if (geometryChanged)
        {
            if (!detail.CommitChanges())
                throw new InvalidOperationException($"Rhino did not commit detail geometry '{recipe.Name}'.");
            detail = document.Objects.FindId(detail.Id) as DetailViewObject
                ?? throw new InvalidOperationException($"Rhino could not find detail '{recipe.Name}' after committing it.");
        }

        var viewportChanged = false;
        if (recipe.CameraLocation is [var lx, var ly, var lz] &&
            recipe.CameraTarget is [var tx, var ty, var tz])
        {
            detail.Viewport.SetCameraLocations(new Point3d(tx, ty, tz), new Point3d(lx, ly, lz));
            viewportChanged = true;
        }
        if (recipe.DisplayModeId is { } modeId)
        {
            using var mode = DisplayModeDescription.GetDisplayMode(modeId);
            if (mode is not null)
            {
                detail.Viewport.DisplayMode = mode;
                viewportChanged = true;
            }
        }
        if (viewportChanged && !detail.CommitViewportChanges())
            throw new InvalidOperationException($"Rhino did not commit viewport settings for detail '{recipe.Name}'.");
        return detail;
    }

    private static void ApplyLayerOverrides(
        RhinoDoc document,
        Guid detailId,
        IReadOnlyList<LayoutPackageLayerOverride> overrides,
        ICollection<string> warnings)
    {
        foreach (var item in overrides)
        {
            var index = document.Layers.FindByFullPath(item.LayerFullPath, -1);
            if (index < 0)
            {
                warnings.Add($"Layer '{item.LayerFullPath}' is unavailable; its detail state was retained as unresolved.");
                continue;
            }
            var layer = new Layer();
            layer.CopyAttributesFrom(document.Layers[index]);
            layer.SetPerViewportVisible(detailId, item.IsVisible);
            document.Layers.Modify(layer, index, quiet: true);
        }
    }

    private static DetailSlotRecipe RemapDetailRecipe(
        RhinoDoc document,
        DetailSlotRecipe recipe,
        IReadOnlyDictionary<Guid, Guid> displayModeMap,
        IReadOnlyDictionary<Guid, Guid> objectMap)
    {
        var layers = recipe.Layers.Select(rule =>
        {
            var index = document.Layers.FindByFullPath(rule.Layer.FullPath, -1);
            return index >= 0
                ? rule with { Layer = new LayerReference(document.Layers[index].Id, rule.Layer.FullPath) }
                : rule;
        }).ToArray();
        var objects = recipe.Objects.Select(rule =>
        {
            var selector = rule.Selector;
            if (selector.Kind == ObjectDisplaySelectorKind.ExactObject && selector.ObjectId is { } objectId)
                selector = selector with { ObjectId = objectMap.GetValueOrDefault(objectId, objectId) };
            else if (selector.Kind == ObjectDisplaySelectorKind.Layer &&
                     !string.IsNullOrWhiteSpace(selector.LayerFullPath))
            {
                var index = document.Layers.FindByFullPath(selector.LayerFullPath, -1);
                if (index >= 0) selector = selector with { LayerId = document.Layers[index].Id };
            }
            return rule with
            {
                Selector = selector,
                DisplayModeId = displayModeMap.GetValueOrDefault(rule.DisplayModeId, rule.DisplayModeId),
            };
        }).ToArray();
        return recipe with
        {
            DisplayModeId = recipe.DisplayModeId is { } modeId
                ? displayModeMap.GetValueOrDefault(modeId, modeId)
                : null,
            LayerRules = layers,
            ObjectDisplayRules = objects,
        };
    }

    private static void ApplyLayerRules(
        RhinoDoc document,
        Guid detailId,
        IReadOnlyList<LayerVisibilityRule> rules,
        ICollection<string> warnings)
    {
        foreach (var rule in rules)
        {
            var layer = document.Layers.FindId(rule.Layer.LayerId);
            if (layer is null)
            {
                var index = document.Layers.FindByFullPath(rule.Layer.FullPath, -1);
                if (index >= 0) layer = document.Layers[index];
            }
            if (layer is null)
            {
                warnings.Add($"Layer '{rule.Layer.FullPath}' is unavailable; its template rule remains unresolved.");
                continue;
            }
            var copy = new Layer();
            copy.CopyAttributesFrom(layer);
            copy.SetPerViewportVisible(detailId, rule.Visibility == LayerVisibilityOverride.Visible);
            document.Layers.Modify(copy, layer.Index, quiet: true);
        }
    }

    private static void ApplyObjectDisplayRules(
        RhinoDoc document,
        Guid detailId,
        IReadOnlyList<ObjectDisplayRule> rules,
        ICollection<string> warnings)
    {
        var layers = document.Layers.Where(layer => !layer.IsDeleted && !layer.IsReference)
            .Select(layer => new LayerSnapshot(
                layer.Id,
                layer.ParentLayerId == Guid.Empty ? null : layer.ParentLayerId,
                layer.FullPath,
                layer.IsVisible))
            .ToDictionary(layer => layer.Id);
        var objects = document.Objects.Where(item => item is not DetailViewObject &&
                                                      item.Attributes.Space == ActiveSpace.ModelSpace)
            .Select(item =>
            {
                var layer = document.Layers[item.Attributes.LayerIndex];
                return new ModelObjectSnapshot(
                    item.Id,
                    item.Attributes.Name,
                    layer.Id,
                    layer.FullPath,
                    item is InstanceObject);
            }).ToDictionary(item => item.Id);
        var scope = new HierarchyScope(HierarchyScopeKind.Detail, detailId);
        var resolved = ViewportAppearanceResolver.Resolve(
            [scope],
            new Dictionary<HierarchyScope, HierarchyViewportRuleSet>
            {
                [scope] = new HierarchyViewportRuleSet(scope, [], rules),
            },
            layers,
            objects);
        foreach (var pair in resolved.Objects)
        {
            var item = document.Objects.FindId(pair.Key);
            using var mode = DisplayModeDescription.GetDisplayMode(pair.Value.DisplayModeId);
            if (item is null || mode is null)
            {
                warnings.Add("An object display-mode rule remains unresolved after import.");
                continue;
            }
            var attributes = item.Attributes.Duplicate();
            if (attributes.SetDisplayModeOverride(mode, detailId))
                document.Objects.ModifyAttributes(item, attributes, quiet: true);
        }
    }

    private static void ImportPageSpaceObjects(
        RhinoDoc document,
        LayoutPackageContents contents,
        IReadOnlyDictionary<Guid, RhinoPageView> pagesBySource,
        IDictionary<Guid, Guid> objectMap,
        IDictionary<Guid, InstanceDefinition> definitionMap,
        IReadOnlyDictionary<string, LayoutPackageConflictResolution> resolutions,
        ICollection<string> warnings)
    {
        using var file = File3dm.FromByteArray(contents.Assets[LayoutPackageManifest.LayoutAssetEntryName]);
        if (file is null) return;
        var selectedIds = contents.Manifest.Sheets.SelectMany(sheet => sheet.PageSpaceObjectIds).ToHashSet();
        foreach (var source in file.Objects.Where(item => selectedIds.Contains(item.Id)))
        {
            if (!pagesBySource.TryGetValue(source.Attributes.ViewportId, out var page)) continue;
            var sourceLayer = SourceLayer(file, source.Attributes.LayerIndex);
            var destinationLayerIndex = document.Layers.FindByFullPath(sourceLayer.FullPath, -1);
            if (destinationLayerIndex < 0)
            {
                warnings.Add($"Page object '{source.Id}' was skipped because layer '{sourceLayer.FullPath}' is unavailable.");
                continue;
            }
            var attributes = source.Attributes.Duplicate();
            attributes.Space = ActiveSpace.PageSpace;
            attributes.ViewportId = page.MainViewport.Id;
            attributes.LayerIndex = destinationLayerIndex;
            Guid newId;
            if (source.Geometry is InstanceReferenceGeometry reference)
            {
                var definition = EnsureDefinition(
                    document, file, contents.Manifest, reference.ParentIdefId,
                    definitionMap, resolutions, warnings);
                newId = definition is null
                    ? Guid.Empty
                    : document.Objects.AddInstanceObject(definition.Index, reference.Xform, attributes);
            }
            else
            {
                var geometry = source.Geometry.Duplicate();
                RemapObjectDependencies(document, file, attributes, geometry, warnings);
                newId = document.Objects.Add(geometry, attributes);
            }
            if (newId != Guid.Empty) objectMap[source.Id] = newId;
            else warnings.Add($"Page object '{source.Id}' could not be imported.");
        }
    }

    private static InstanceDefinition? EnsureDefinition(
        RhinoDoc document,
        File3dm file,
        LayoutPackageManifest manifest,
        Guid sourceId,
        IDictionary<Guid, InstanceDefinition> map,
        IReadOnlyDictionary<string, LayoutPackageConflictResolution> resolutions,
        ICollection<string> warnings)
    {
        if (map.TryGetValue(sourceId, out var mapped)) return mapped;
        var source = file.AllInstanceDefinitions.FirstOrDefault(item => item.Id == sourceId);
        if (source is null) return null;
        var existing = document.InstanceDefinitions.Find(source.Name);
        var packaged = manifest.TitleBlocks.FirstOrDefault(item => item.SourceId == sourceId);
        var sameContent = existing is not null && packaged is not null &&
                          string.Equals(FingerprintDefinition(existing), packaged.Fingerprint,
                              StringComparison.OrdinalIgnoreCase);
        var key = $"{LayoutPackageDependencyKind.TitleBlockDefinition}:{source.Name}";
        var resolution = resolutions.GetValueOrDefault(key, LayoutPackageConflictResolution.ImportRenamedCopy);
        if (existing is not null &&
            (sameContent || resolution == LayoutPackageConflictResolution.ReuseDestination))
        {
            map[sourceId] = existing;
            return existing;
        }

        var geometry = new List<GeometryBase>();
        var attributes = new List<ObjectAttributes>();
        foreach (var sourceObjectId in source.GetObjectIds())
        {
            var sourceObject = file.Objects.FirstOrDefault(item => item.Id == sourceObjectId);
            if (sourceObject is null) continue;
            GeometryBase duplicated;
            if (sourceObject.Geometry is InstanceReferenceGeometry nested)
            {
                var nestedDefinition = EnsureDefinition(
                    document, file, manifest, nested.ParentIdefId, map, resolutions, warnings);
                if (nestedDefinition is null) continue;
                duplicated = new InstanceReferenceGeometry(nestedDefinition.Id, nested.Xform);
            }
            else duplicated = sourceObject.Geometry.Duplicate();

            var memberAttributes = sourceObject.Attributes.Duplicate();
            var sourceLayer = SourceLayer(file, sourceObject.Attributes.LayerIndex);
            var layerIndex = document.Layers.FindByFullPath(sourceLayer.FullPath, -1);
            if (layerIndex < 0)
            {
                warnings.Add($"Block member on layer '{sourceLayer.FullPath}' was omitted from '{source.Name}'.");
                continue;
            }
            memberAttributes.LayerIndex = layerIndex;
            RemapObjectDependencies(document, file, memberAttributes, duplicated, warnings);
            geometry.Add(duplicated);
            attributes.Add(memberAttributes);
        }
        if (geometry.Count == 0) return null;
        var index = document.InstanceDefinitions.Add(
            UniqueName(source.Name, document.InstanceDefinitions.Select(item => item.Name)),
            source.Description,
            Point3d.Origin,
            geometry,
            attributes);
        if (index < 0) return null;
        var created = document.InstanceDefinitions[index];
        map[sourceId] = created;
        return created;
    }

    private static void RemapObjectDependencies(
        RhinoDoc document,
        File3dm file,
        ObjectAttributes attributes,
        GeometryBase geometry,
        ICollection<string> warnings)
    {
        if (attributes.LinetypeIndex >= 0)
        {
            var source = file.AllLinetypes.FirstOrDefault(item => item.Index == attributes.LinetypeIndex);
            if (source is not null)
            {
                var destination = document.Linetypes.FindName(source.Name);
                var index = destination?.Index ?? document.Linetypes.Add(source);
                if (index >= 0) attributes.LinetypeIndex = index;
                else warnings.Add($"Linetype '{source.Name}' could not be imported.");
            }
        }

        if (attributes.MaterialIndex >= 0)
        {
            var source = file.AllMaterials.FirstOrDefault(item => item.Index == attributes.MaterialIndex);
            if (source is not null)
            {
                var destinationIndex = document.Materials.Find(source.Name, true);
                var index = destinationIndex >= 0 ? destinationIndex : document.Materials.Add(source);
                if (index >= 0) attributes.MaterialIndex = index;
                else warnings.Add($"Material '{source.Name}' could not be imported.");
            }
        }

        if (geometry is AnnotationBase annotation)
        {
            var source = file.AllDimStyles.FirstOrDefault(item => item.Id == annotation.DimensionStyleId);
            if (source is not null)
            {
                var destination = document.DimStyles.FindName(source.Name);
                var index = destination?.Index ?? document.DimStyles.Add(source, false);
                if (index >= 0) annotation.DimensionStyleId = document.DimStyles[index].Id;
                else warnings.Add($"Dimension style '{source.Name}' could not be imported.");
            }
        }

        if (geometry is Hatch hatch && hatch.PatternIndex >= 0)
        {
            var source = file.AllHatchPatterns.FirstOrDefault(item => item.Index == hatch.PatternIndex);
            if (source is not null)
            {
                var destination = document.HatchPatterns.FindName(source.Name);
                var index = destination?.Index ?? document.HatchPatterns.Add(source);
                if (index >= 0) hatch.PatternIndex = index;
                else warnings.Add($"Hatch pattern '{source.Name}' could not be imported.");
            }
        }
    }

    private static DocumentState RemapState(
        RhinoDoc document,
        DocumentState before,
        LayoutPackageManifest manifest,
        LayoutPackageImportMode mode,
        bool importProjectInformation,
        IReadOnlyDictionary<Guid, Guid> folderMap,
        IReadOnlyDictionary<Guid, RhinoPageView> pagesBySource,
        IReadOnlyDictionary<Guid, Guid> detailsBySource,
        IReadOnlyDictionary<Guid, Guid> objectMap,
        IReadOnlyDictionary<Guid, InstanceDefinition> definitionMap,
        IReadOnlyDictionary<Guid, Guid> displayModeMap,
        IReadOnlyDictionary<string, string> namedViewMap,
        IReadOnlyDictionary<string, LayoutPackageConflictResolution> resolutions,
        ICollection<string> warnings)
    {
        var rootId = before.RootFolderId;
        var folders = mode == LayoutPackageImportMode.Merge
            ? before.Folders.ToList()
            : before.Folders.Where(folder => folder.Id == rootId).ToList();
        if (mode == LayoutPackageImportMode.Merge)
        {
            var wrapperName = UniqueName(manifest.SourceDocumentName,
                folders.Where(folder => folder.ParentId == rootId).Select(folder => folder.Name));
            folders.Add(new FolderRecord(
                folderMap[manifest.FoundryState.RootFolderId],
                rootId,
                wrapperName,
                folders.Where(folder => folder.ParentId == rootId).Select(folder => folder.Order)
                    .DefaultIfEmpty(-1).Max() + 1));
        }
        foreach (var source in manifest.FoundryState.Folders.Where(folder =>
                     folder.Id != manifest.FoundryState.RootFolderId))
            folders.Add(source with
            {
                Id = folderMap[source.Id],
                ParentId = source.ParentId is { } parent ? folderMap[parent] : rootId,
            });

        var metadata = mode == LayoutPackageImportMode.Replace
            ? manifest.FoundryState.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : before.Metadata.Concat(manifest.FoundryState.Metadata.Where(pair => !before.Metadata.ContainsKey(pair.Key)))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        var sheets = mode == LayoutPackageImportMode.Merge
            ? before.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value)
            : new Dictionary<Guid, SheetRecord>();
        foreach (var source in manifest.Sheets)
        {
            var page = pagesBySource[source.SourcePageViewId];
            var titleBlock = source.TitleBlock is { } role && objectMap.TryGetValue(role.InstanceObjectId, out var objectId)
                ? role with
                {
                    InstanceObjectId = objectId,
                    InstanceDefinitionId = definitionMap.TryGetValue(role.InstanceDefinitionId, out var definition)
                        ? definition.Id
                        : role.InstanceDefinitionId,
                }
                : null;
            var sourceFolderName = manifest.FoundryState.Folders
                .FirstOrDefault(folder => folder.Id == source.SourceFolderId)?.Name ?? string.Empty;
            var destinationFolderName = folders
                .FirstOrDefault(folder => folder.Id == folderMap[source.SourceFolderId])?.Name ?? string.Empty;
            var remappedViews = source.NamingBinding?.NamedViews
                .Where(pair => detailsBySource.ContainsKey(pair.Key))
                .ToDictionary(
                    pair => detailsBySource[pair.Key],
                    pair => namedViewMap.GetValueOrDefault(pair.Value, pair.Value)) ?? [];
            var namingBinding = source.NamingBinding is { } binding &&
                                string.Equals(page.PageName, binding.LastGeneratedName, StringComparison.Ordinal) &&
                                BindingSourcesUnchanged(
                                    binding,
                                    source,
                                    manifest.FoundryState.Metadata,
                                    metadata,
                                    sourceFolderName,
                                    destinationFolderName,
                                    remappedViews)
                ? binding with
                {
                    NamedViewAssignments = remappedViews,
                }
                : null;
            sheets[page.MainViewport.Id] = new SheetRecord(
                page.MainViewport.Id,
                folderMap[source.SourceFolderId],
                source.Order,
                source.Tags,
                source.Metadata,
                titleBlock,
                source.IncludeInPrintAll,
                source.TitleBlockData,
                namingBinding,
                source.Notes ?? string.Empty);
        }

        var recovery = (mode == LayoutPackageImportMode.Merge ? before.Recovery : [])
            .Concat(manifest.FoundryState.Recovery)
            .ToList();
        foreach (var warning in warnings.Distinct(StringComparer.Ordinal))
            recovery.Add(new ImportRecoveryRecord("import", manifest.SourceDocumentName, warning));

        var rules = mode == LayoutPackageImportMode.Merge
            ? before.DisplayRules.ToList()
            : [];
        foreach (var rule in manifest.FoundryState.DisplayRules)
        {
            var missingObjects = rule.ObjectIds.Where(id => document.Objects.FindId(id) is null).ToArray();
            var selectors = rule.Targets.Select(selector => selector.Kind switch
            {
                HierarchySelectorKind.Folder when folderMap.TryGetValue(selector.Id, out var id) => selector with { Id = id },
                HierarchySelectorKind.Sheet when pagesBySource.TryGetValue(selector.Id, out var page) =>
                    selector with { Id = page.MainViewport.Id },
                HierarchySelectorKind.Detail when detailsBySource.TryGetValue(selector.Id, out var detail) =>
                    selector with { Id = detail },
                _ => selector,
            }).ToArray();
            if (missingObjects.Length > 0)
            {
                recovery.Add(new ImportRecoveryRecord(
                    "display-rule",
                    rule.Name,
                    $"Disabled because {missingObjects.Length} source model object(s) are unavailable.",
                    rule.Id));
            }
            rules.Add(rule with
            {
                Id = Guid.NewGuid(),
                Enabled = rule.Enabled && missingObjects.Length == 0,
                Targets = selectors,
                DisplayModeId = displayModeMap.GetValueOrDefault(rule.DisplayModeId, rule.DisplayModeId),
            });
        }

        var templates = mode == LayoutPackageImportMode.Merge ? before.Templates.ToList() : [];
        var templateNames = templates.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var template in manifest.FoundryState.Templates)
        {
            var conflictKey = $"{LayoutPackageDependencyKind.Template}:{template.Name}";
            var resolution = resolutions.GetValueOrDefault(
                conflictKey, LayoutPackageConflictResolution.ImportRenamedCopy);
            if (templateNames.Contains(template.Name) &&
                resolution == LayoutPackageConflictResolution.ReuseDestination) continue;
            var name = UniqueName(template.Name, templateNames);
            templates.Add(template with
            {
                Id = Guid.NewGuid(),
                Name = name,
                SourcePageViewId = template.SourcePageViewId is { } pageId && pagesBySource.TryGetValue(pageId, out var page)
                    ? page.MainViewport.Id
                    : null,
                DetailSlots = template.DetailSlots.Select(slot => slot with
                {
                    Id = Guid.NewGuid(),
                    DisplayModeId = slot.DisplayModeId is { } modeId
                        ? displayModeMap.GetValueOrDefault(modeId, modeId)
                        : null,
                    DefaultNamedView = slot.DefaultNamedView is { } viewName
                        ? namedViewMap.GetValueOrDefault(viewName, viewName)
                        : null,
                    LayerRules = slot.Layers.Select(rule =>
                    {
                        var layerIndex = document.Layers.FindByFullPath(rule.Layer.FullPath, -1);
                        return layerIndex >= 0
                            ? rule with
                            {
                                Layer = new LayerReference(
                                    document.Layers[layerIndex].Id,
                                    rule.Layer.FullPath),
                            }
                            : rule;
                    }).ToArray(),
                    ObjectDisplayRules = slot.Objects.Select(rule =>
                    {
                        var selector = rule.Selector;
                        if (selector.Kind == ObjectDisplaySelectorKind.ExactObject &&
                            selector.ObjectId is { } sourceObjectId)
                            selector = selector with
                            {
                                ObjectId = objectMap.GetValueOrDefault(sourceObjectId, sourceObjectId),
                            };
                        else if (selector.Kind == ObjectDisplaySelectorKind.Layer &&
                                 !string.IsNullOrWhiteSpace(selector.LayerFullPath))
                        {
                            var layerIndex = document.Layers.FindByFullPath(selector.LayerFullPath, -1);
                            if (layerIndex >= 0)
                                selector = selector with { LayerId = document.Layers[layerIndex].Id };
                        }
                        return rule with
                        {
                            Selector = selector,
                            DisplayModeId = displayModeMap.GetValueOrDefault(
                                rule.DisplayModeId,
                                rule.DisplayModeId),
                        };
                    }).ToArray(),
                }).ToArray(),
                TitleBlock = template.TitleBlock is { } titleBlock &&
                             definitionMap.TryGetValue(titleBlock.InstanceDefinitionId, out var mappedDefinition)
                    ? titleBlock with
                    {
                        InstanceDefinitionId = mappedDefinition.Id,
                        InstanceDefinitionName = mappedDefinition.Name,
                    }
                    : template.TitleBlock,
            });
            templateNames.Add(name);
        }

        HierarchyScope? RemapScope(HierarchyScope source) => source.Kind switch
        {
            HierarchyScopeKind.Folder when folderMap.TryGetValue(source.Id, out var folderId) =>
                new HierarchyScope(source.Kind, folderId),
            HierarchyScopeKind.Sheet when pagesBySource.TryGetValue(source.Id, out var page) =>
                new HierarchyScope(source.Kind, page.MainViewport.Id),
            HierarchyScopeKind.Detail when detailsBySource.TryGetValue(source.Id, out var detailId) =>
                new HierarchyScope(source.Kind, detailId),
            _ => null,
        };

        LayerVisibilityRule RemapLayerRule(LayerVisibilityRule rule)
        {
            var index = document.Layers.FindByFullPath(rule.Layer.FullPath, -1);
            return index >= 0
                ? rule with { Layer = new LayerReference(document.Layers[index].Id, rule.Layer.FullPath) }
                : rule;
        }

        ObjectDisplayRule RemapObjectRule(ObjectDisplayRule rule)
        {
            var selector = rule.Selector;
            if (selector.Kind == ObjectDisplaySelectorKind.ExactObject && selector.ObjectId is { } sourceObjectId)
                selector = selector with { ObjectId = objectMap.GetValueOrDefault(sourceObjectId, sourceObjectId) };
            else if (selector.Kind == ObjectDisplaySelectorKind.Layer &&
                     !string.IsNullOrWhiteSpace(selector.LayerFullPath))
            {
                var index = document.Layers.FindByFullPath(selector.LayerFullPath, -1);
                if (index >= 0) selector = selector with { LayerId = document.Layers[index].Id };
            }
            return rule with
            {
                Selector = selector,
                DisplayModeId = displayModeMap.GetValueOrDefault(rule.DisplayModeId, rule.DisplayModeId),
            };
        }

        var appearanceRules = mode == LayoutPackageImportMode.Merge
            ? before.AppearanceRules.ToList()
            : [];
        foreach (var sourceRules in manifest.FoundryState.AppearanceRules)
        {
            if (RemapScope(sourceRules.Scope) is not { } targetScope) continue;
            appearanceRules.Add(new HierarchyViewportRuleSet(
                targetScope,
                sourceRules.LayerRules.Select(RemapLayerRule).ToArray(),
                sourceRules.ObjectDisplayRules.Select(RemapObjectRule).ToArray()));
        }

        var registrations = mode == LayoutPackageImportMode.Merge
            ? before.TemplateRegistrations.ToList()
            : [];
        var registrationMap = new Dictionary<Guid, Guid>();
        foreach (var sourceRegistration in manifest.FoundryState.TemplateRegistrations)
        {
            if (RemapScope(sourceRegistration.Source) is not { } sourceScope) continue;
            var id = Guid.NewGuid();
            registrationMap[sourceRegistration.Id] = id;
            registrations.Add(new CapabilityTemplateRegistration(
                id,
                sourceScope,
                sourceRegistration.Capabilities));
        }

        var capabilityLinks = mode == LayoutPackageImportMode.Merge
            ? before.TemplateLinks.ToList()
            : [];
        foreach (var sourceLink in manifest.FoundryState.TemplateLinks)
        {
            if (RemapScope(sourceLink.Target) is not { } targetScope ||
                !registrationMap.TryGetValue(sourceLink.SourceRegistrationId, out var registrationId))
                continue;
            capabilityLinks.Add(sourceLink with
            {
                Id = Guid.NewGuid(),
                Target = targetScope,
                SourceRegistrationId = registrationId,
                DetailMappings = sourceLink.DetailMappings.Select(mapping => new TemplateDetailMapping(
                    detailsBySource.GetValueOrDefault(mapping.SourceDetailViewportId,
                        mapping.SourceDetailViewportId),
                    detailsBySource.GetValueOrDefault(mapping.TargetDetailViewportId,
                        mapping.TargetDetailViewportId))).ToArray(),
                LastResolved = sourceLink.LastResolved,
            });
        }

        var appearanceStates = mode == LayoutPackageImportMode.Merge
            ? before.AppearanceStates.ToList()
            : [];
        var appearanceStateMap = new Dictionary<Guid, Guid>();
        foreach (var sourceState in manifest.FoundryState.AppearanceStates)
        {
            if (!folderMap.TryGetValue(sourceState.FolderId, out var folderId)) continue;
            var id = Guid.NewGuid();
            appearanceStateMap[sourceState.Id] = id;
            var usedNames = appearanceStates.Where(item => item.FolderId == folderId)
                .Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = UniqueImportedName(sourceState.Name, usedNames);
            appearanceStates.Add(sourceState with
            {
                Id = id,
                FolderId = folderId,
                Name = name,
                LayerRules = sourceState.LayerRules.Select(RemapLayerRule).ToArray(),
                ObjectDisplayRules = sourceState.ObjectDisplayRules.Select(RemapObjectRule).ToArray(),
            });
        }

        var appearanceAssignments = mode == LayoutPackageImportMode.Merge
            ? before.StateAssignments.ToList()
            : [];
        foreach (var sourceAssignment in manifest.FoundryState.StateAssignments)
        {
            if (RemapScope(sourceAssignment.Target) is not { } target ||
                !appearanceStateMap.TryGetValue(sourceAssignment.StateId, out var stateId))
                continue;
            appearanceAssignments.RemoveAll(item => item.Target == target);
            appearanceAssignments.Add(sourceAssignment with
            {
                Id = Guid.NewGuid(),
                Target = target,
                StateId = stateId,
            });
        }

        var canvas = RemapCanvas(before.Canvas, manifest.FoundryState.Canvas, mode, folderMap, pagesBySource);
        return new DocumentState(
            DocumentState.CurrentSchemaVersion,
            rootId,
            folders,
            sheets,
            rules,
            metadata,
            templates,
            canvas,
            recovery,
            before.DedicatedDetailLayerId,
            LayoutPackageProjectInformationPolicy.Resolve(
                before.ProjectInfo,
                manifest.FoundryState.ProjectInfo,
                mode,
                importProjectInformation),
            appearanceRules,
            registrations,
            capabilityLinks,
            appearanceStates,
            appearanceAssignments);
    }

    private static bool BindingSourcesUnchanged(
        SheetNamingBinding binding,
        LayoutPackageSheet sheet,
        IReadOnlyDictionary<string, string> sourceMetadata,
        IReadOnlyDictionary<string, string> destinationMetadata,
        string sourceFolderName,
        string destinationFolderName,
        IReadOnlyDictionary<Guid, string> remappedViews)
    {
        bool Uses(string token) => binding.Pattern.Contains($"{{{token}}}", StringComparison.OrdinalIgnoreCase);
        string MetadataValue(IReadOnlyDictionary<string, string> documentValues, string key) =>
            sheet.Metadata.GetValueOrDefault(key, documentValues.GetValueOrDefault(key, string.Empty));
        if (Uses("folder") && !string.Equals(sourceFolderName, destinationFolderName, StringComparison.Ordinal))
            return false;
        foreach (var token in new[] { "project", "discipline" })
            if (Uses(token) && !string.Equals(
                    MetadataValue(sourceMetadata, token),
                    MetadataValue(destinationMetadata, token),
                    StringComparison.Ordinal))
                return false;
        return !Uses("view") || binding.NamedViews.Values.SequenceEqual(remappedViews.Values);
    }

    private static string UniqueImportedName(string source, IReadOnlySet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(source) ? "Imported state" : source.Trim();
        if (!usedNames.Contains(baseName)) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!usedNames.Contains(candidate)) return candidate;
        }
    }

    private static ObserverCanvasState RemapCanvas(
        ObserverCanvasState before,
        ObserverCanvasState imported,
        LayoutPackageImportMode mode,
        IReadOnlyDictionary<Guid, Guid> folderMap,
        IReadOnlyDictionary<Guid, RhinoPageView> pagesBySource)
    {
        var folderOrigins = mode == LayoutPackageImportMode.Merge
            ? before.FolderOrigins.ToDictionary(pair => pair.Key, pair => pair.Value)
            : new Dictionary<Guid, ObserverPointRecord>();
        var sheetPlacements = mode == LayoutPackageImportMode.Merge
            ? before.SheetPlacements.ToDictionary(pair => pair.Key, pair => pair.Value)
            : new Dictionary<Guid, ObserverPointRecord>();
        var offsetX = mode == LayoutPackageImportMode.Merge
            ? sheetPlacements.Values.Select(point => point.X).DefaultIfEmpty(0).Max() + 600
            : 0;
        foreach (var pair in imported.FolderOrigins)
            if (folderMap.TryGetValue(pair.Key, out var id))
                folderOrigins[id] = new ObserverPointRecord(pair.Value.X + offsetX, pair.Value.Y);
        foreach (var pair in imported.SheetPlacements)
            if (pagesBySource.TryGetValue(pair.Key, out var page))
                sheetPlacements[page.MainViewport.Id] = new ObserverPointRecord(pair.Value.X + offsetX, pair.Value.Y);
        return new ObserverCanvasState(
            ObserverCanvasState.CurrentLayoutAlgorithmVersion,
            folderOrigins,
            sheetPlacements);
    }

    private RhinoDoc RequireDocument(uint serial, long revision)
    {
        var document = RhinoDoc.ActiveDoc
            ?? throw new InvalidOperationException("There is no active Rhino document.");
        if (document.RuntimeSerialNumber != serial)
            throw new InvalidOperationException("The active Rhino document changed.");
        if (_revisionTracker.Current(document) != revision)
            throw new InvalidOperationException("The Rhino document changed. Refresh and try again.");
        return document;
    }

    private static DocumentState WithCurrentPageRecords(RhinoDoc document, DocumentState state)
    {
        var folders = state.Folders.Select(folder => folder.Id).ToHashSet();
        var sheets = state.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var entry in document.Views.GetPageViews().Select((page, index) => (page, index)))
        {
            if (!sheets.ContainsKey(entry.page.MainViewport.Id))
                sheets[entry.page.MainViewport.Id] = new SheetRecord(
                    entry.page.MainViewport.Id,
                    folders.Contains(state.RootFolderId) ? state.RootFolderId : WellKnownIds.UnorganizedFolderId,
                    entry.index,
                    [],
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    null);
        }
        return state with { Sheets = sheets };
    }

    private static string ExportDisplayModeFingerprint(DisplayModeDescription mode)
    {
        var path = Path.Combine(Path.GetTempPath(), $"LayoutFoundry-{Guid.NewGuid():N}.ini");
        try
        {
            return DisplayModeDescription.ExportToFile(mode, path) && File.Exists(path)
                ? LayoutPackageArchive.Sha256(File.ReadAllBytes(path))
                : Fingerprint($"{mode.Id:N}|{mode.LocalName}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string FingerprintDefinition(File3dm file, InstanceDefinitionGeometry definition)
    {
        uint crc = 0;
        var count = 0;
        foreach (var objectId in definition.GetObjectIds().Order())
        {
            var item = file.Objects.FirstOrDefault(candidate => candidate.Id == objectId);
            if (item is null) continue;
            crc = item.Geometry.DataCRC(crc);
            count++;
        }
        return $"{count}:{crc:x8}";
    }

    private static string FingerprintDefinition(InstanceDefinition definition)
    {
        uint crc = 0;
        var objects = definition.GetObjects().OrderBy(item => item.Id).ToArray();
        foreach (var item in objects) crc = item.Geometry.DataCRC(crc);
        return $"{objects.Length}:{crc:x8}";
    }

    private static UnitSystem ParseUnitSystem(string value) =>
        Enum.TryParse<UnitSystem>(value, true, out var unit) ? unit : UnitSystem.Millimeters;

    private static Layer SourceLayer(File3dm file, int layerIndex) =>
        file.AllLayers.FirstOrDefault(layer => layer.Index == layerIndex)
        ?? throw new InvalidDataException($"The package references missing layer index {layerIndex}.");

    private static string UniqueName(string requested, IEnumerable<string> existingNames)
    {
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseName = string.IsNullOrWhiteSpace(requested) ? "Imported layouts" : requested.Trim();
        if (!names.Contains(baseName)) return baseName;
        for (var index = 2; ; index++)
        {
            var candidate = $"{baseName} ({index})";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private static string UniqueName(string requested, ISet<string> existingNames)
    {
        var name = UniqueName(requested, existingNames.AsEnumerable());
        existingNames.Add(name);
        return name;
    }

    private static void RemoveCreatedPages(IEnumerable<RhinoPageView> pages)
    {
        foreach (var page in pages.Reverse())
            try { page.Close(); }
            catch { }
    }

    private static void RemoveImportedDisplayModes(IEnumerable<Guid> ids)
    {
        foreach (var id in ids.Reverse())
            try { DisplayModeDescription.DeleteDisplayMode(id); }
            catch { }
    }

    private static Task<T> RunOnUiThread<T>(Func<T> action)
    {
        if (RhinoApp.InvokeRequired)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            RhinoApp.InvokeOnUiThread(() =>
            {
                try { completion.SetResult(action()); }
                catch (Exception exception) { completion.SetException(exception); }
            });
            return completion.Task;
        }
        return Task.FromResult(action());
    }

    private sealed record CapturedPackage(
        LayoutPackageManifest Manifest,
        IReadOnlyDictionary<string, byte[]> Assets);
}
