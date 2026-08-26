using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Naming;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class NamingEngineTests
{
    [Fact]
    public void TokensAndSequenceProduceDeterministicPreview()
    {
        var request = new NamingRequest(
            "{project}-{discipline}-{index:000}-{view}",
            [
                Item("Old 1", ("project", "Foundry"), ("discipline", "A"), ("view", "Plan")),
                Item("Old 2", ("project", "Foundry"), ("discipline", "A"), ("view", "Section")),
            ],
            10,
            5);

        var preview = NamingEngine.Preview(request);

        Assert.True(preview.CanApply);
        Assert.Equal("Foundry-A-010-Plan", preview.Entries[0].ProposedName);
        Assert.Equal("Foundry-A-015-Section", preview.Entries[1].ProposedName);
        Assert.Empty(preview.Diagnostics);
    }

    [Fact]
    public void DuplicateNamesBlockApplyCaseInsensitively()
    {
        var request = new NamingRequest(
            "{view}",
            [Item("Old 1", ("view", "Plan")), Item("Old 2", ("view", "plan"))],
            1,
            1);

        var preview = NamingEngine.Preview(request);

        Assert.False(preview.CanApply);
        Assert.Equal(2, preview.Diagnostics.Count(item => item.Code == "NAME_DUPLICATE"));
    }

    [Fact]
    public void UnknownTokenBlocksApply()
    {
        var preview = NamingEngine.Preview(
            new NamingRequest("{unsupported}-{index}", [Item("Old")], 1, 1));

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Diagnostics, item => item.Code == "NAME_TOKEN_UNKNOWN");
    }

    [Fact]
    public void MissingTokenWarnsButNonEmptyResultCanApply()
    {
        var preview = NamingEngine.Preview(
            new NamingRequest("Sheet-{view}-{index:00}", [Item("Old")], 1, 1));

        Assert.True(preview.CanApply);
        Assert.Equal("Sheet--01", preview.Entries.Single().ProposedName);
        Assert.Contains(
            preview.Diagnostics,
            item => item.Code == "NAME_TOKEN_MISSING" && item.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void EmptyResultBlocksApply()
    {
        var preview = NamingEngine.Preview(
            new NamingRequest("{view}", [Item("Old")], 1, 1));

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Diagnostics, item => item.Code == "NAME_EMPTY");
    }

    private static NamingItem Item(
        string currentName,
        params (string Key, string Value)[] values)
    {
        return new NamingItem(
            Guid.NewGuid(),
            currentName,
            values.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));
    }
}

