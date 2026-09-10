using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DetailCaptionTests
{
    private static readonly Guid Root = Guid.NewGuid(), Child = Guid.NewGuid(), Page = Guid.NewGuid(), Detail = Guid.NewGuid();
    private static readonly IReadOnlyDictionary<Guid, FolderRecord> Folders = new Dictionary<Guid, FolderRecord>
    { [Root] = new(Root, null, "Root", 0), [Child] = new(Child, Root, "Child", 0) };
    private static EffectiveDetailCaption Resolve(IReadOnlyDictionary<string, string> values, bool parallel = true) =>
        DetailCaptions.Resolve(values, Folders, Child, Page, Detail, parallel);

    [Fact]
    public void DefaultsAndPerspectiveSuppression()
    {
        Assert.Equal(new(true, true), Resolve(new Dictionary<string, string>()));
        Assert.Equal(new(true, false), Resolve(new Dictionary<string, string>(), false));
    }

    [Fact]
    public void IndependentNearestOverridesAndReset()
    {
        var values = DetailCaptions.Write(new Dictionary<string, string>(), new(HierarchyScopeKind.Folder, Root), new(CaptionVisibility.Hide, CaptionVisibility.Hide));
        values = DetailCaptions.Write(values, new(HierarchyScopeKind.Sheet, Page), new(CaptionVisibility.Show));
        Assert.Equal(new(true, false), Resolve(values));
        values = DetailCaptions.Write(values, new(HierarchyScopeKind.Detail, Detail), new(CaptionVisibility.Inherit, CaptionVisibility.Show));
        Assert.Equal(new(true, true), Resolve(values));
        Assert.Equal(new(true, false), Resolve(values, false));
        values = DetailCaptions.Write(values, new(HierarchyScopeKind.Detail, Detail), new());
        Assert.Equal(new(true, false), Resolve(values));
    }

    [Fact]
    public void FieldUsesObjectGuidAndSeparatorOnlyWhenNeeded()
    {
        Assert.Equal("Plan", DetailCaptions.Text("Plan", Detail, new(true, false)));
        Assert.Equal("", DetailCaptions.Text("Plan", Detail, new(false, false)));
        Assert.Equal($"%<DetailScale(\"{Detail:D}\",\"1:#\")>%", DetailCaptions.Text("Plan", Detail, new(false, true)));
        Assert.StartsWith("Plan | %<", DetailCaptions.Text("Plan", Detail, new(true, true)));
    }

    [Fact]
    public void CopiedPreferencesDoNotKeepSourceIdentifiers()
    {
        var source = DetailCaptions.Write(new Dictionary<string, string>(), new(HierarchyScopeKind.Detail, Detail), new(CaptionVisibility.Hide));
        var target = Guid.NewGuid();
        var copy = DetailCaptions.CopyScopes(new Dictionary<string, string>(), source, HierarchyScopeKind.Detail,
            new Dictionary<Guid, Guid> { [Detail] = target });
        Assert.Equal(CaptionVisibility.Hide, DetailCaptions.Read(copy, new(HierarchyScopeKind.Detail, target)).Name);
        Assert.Equal(CaptionVisibility.Inherit, DetailCaptions.Read(copy, new(HierarchyScopeKind.Detail, Detail)).Name);
    }

    [Fact]
    public void PreferencesRoundTripAndOldDocumentsRemainUnmanaged()
    {
        var state = DocumentState.Empty();
        state = state with { Metadata = DetailCaptions.Write(state.Metadata, new(HierarchyScopeKind.Folder, state.RootFolderId), new(CaptionVisibility.Hide)) };
        var restored = DocumentStateSerializer.Deserialize(DocumentStateSerializer.Serialize(state));
        Assert.Equal(CaptionVisibility.Hide, DetailCaptions.Read(restored.Metadata, new(HierarchyScopeKind.Folder, state.RootFolderId)).Name);
        Assert.False(new DetailSnapshot(Detail, "Old", Guid.Empty, "").HasManagedCaption);
    }

    [Fact]
    public void PlannerRejectsStaleOrMissingTarget()
    {
        var snapshot = new DocumentSnapshot(1, 2, Root, Folders, new Dictionary<Guid, SheetSnapshot>(), new HashSet<Guid>(), new HashSet<Guid>());
        var planner = new SetDetailCaptionsPlanner();
        Assert.True(planner.Plan(new(1, 2, new(HierarchyScopeKind.Folder, Root), new()), snapshot).CanApply);
        Assert.False(planner.Plan(new(1, 1, new(HierarchyScopeKind.Folder, Root), new()), snapshot).CanApply);
        Assert.False(planner.Plan(new(1, 2, new(HierarchyScopeKind.Detail, Detail), new()), snapshot).CanApply);
    }
}
