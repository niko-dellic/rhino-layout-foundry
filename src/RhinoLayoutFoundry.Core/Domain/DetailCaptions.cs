namespace RhinoLayoutFoundry.Core.Domain;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<CaptionVisibility>))]
public enum CaptionVisibility { Inherit, Show, Hide }

public sealed record DetailCaptionSettings(CaptionVisibility Name = CaptionVisibility.Inherit,
    CaptionVisibility Scale = CaptionVisibility.Inherit);

public readonly record struct EffectiveDetailCaption(bool Name, bool Scale);

/// <summary>Caption preferences live in the existing versioned metadata bag. Missing keys inherit.</summary>
public static class DetailCaptions
{
    public const string ManagedKey = "RhinoLayoutFoundry.Caption.Managed";
    public const string OwnerKey = "RhinoLayoutFoundry.Caption.DetailObjectId";
    public const string SourceViewportKey = "RhinoLayoutFoundry.Caption.SourceViewportId";
    private static string Key(HierarchyScope scope, string component) => $"caption.{scope.Kind}.{scope.Id:D}.{component}";

    public static DetailCaptionSettings Read(IReadOnlyDictionary<string, string> metadata, HierarchyScope scope)
    {
        CaptionVisibility Get(string component) => metadata.TryGetValue(Key(scope, component), out var value)
            && Enum.TryParse<CaptionVisibility>(value, out var result) && Enum.IsDefined(result)
                ? result : CaptionVisibility.Inherit;
        return new(Get("name"), Get("scale"));
    }

    public static IReadOnlyDictionary<string, string> Write(IReadOnlyDictionary<string, string> metadata,
        HierarchyScope scope, DetailCaptionSettings settings)
    {
        var result = new Dictionary<string, string>(metadata, StringComparer.Ordinal);
        void Set(string component, CaptionVisibility value)
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(settings));
            if (value == CaptionVisibility.Inherit) result.Remove(Key(scope, component));
            else result[Key(scope, component)] = value.ToString();
        }
        Set("name", settings.Name); Set("scale", settings.Scale);
        return result;
    }

    public static EffectiveDetailCaption Resolve(IReadOnlyDictionary<string, string> metadata,
        IReadOnlyDictionary<Guid, FolderRecord> folders, Guid folderId, Guid? pageId = null,
        Guid? detailId = null, bool parallel = true)
    {
        var name = CaptionVisibility.Inherit;
        var scale = CaptionVisibility.Inherit;
        void ReadScope(HierarchyScope scope)
        {
            var settings = Read(metadata, scope);
            if (name == CaptionVisibility.Inherit) name = settings.Name;
            if (scale == CaptionVisibility.Inherit) scale = settings.Scale;
        }
        if (detailId is { } detail) ReadScope(new(HierarchyScopeKind.Detail, detail));
        if (pageId is { } page) ReadScope(new(HierarchyScopeKind.Sheet, page));
        var visited = new HashSet<Guid>();
        while (visited.Add(folderId))
        {
            ReadScope(new(HierarchyScopeKind.Folder, folderId));
            if (!folders.TryGetValue(folderId, out var folder) || folder.ParentId is not { } parent) break;
            folderId = parent;
        }
        return new(name != CaptionVisibility.Hide, parallel && scale != CaptionVisibility.Hide);
    }

    public static string Text(string name, Guid detailObjectId, EffectiveDetailCaption visibility)
    {
        var title = visibility.Name ? name.Trim() : "";
        var scale = visibility.Scale ? $"%<DetailScale(\"{detailObjectId:D}\",\"1:#\")>%" : "";
        return title.Length > 0 && scale.Length > 0 ? title + " | " + scale : title + scale;
    }

    public static IReadOnlyDictionary<string, string> CopyScopes(IReadOnlyDictionary<string, string> destination,
        IReadOnlyDictionary<string, string> source, HierarchyScopeKind kind, IReadOnlyDictionary<Guid, Guid> map)
    {
        foreach (var pair in map) destination = Write(destination, new(kind, pair.Value), Read(source, new(kind, pair.Key)));
        return destination;
    }
}
