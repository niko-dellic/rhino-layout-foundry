using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Extensibility;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DrawingSetSpecificationTests
{
    [Fact]
    public void CompleteBatchMayIncludeExplicitBlankPlaceholder()
    {
        var proposal = Proposal();
        var blank = proposal.Sheets[0] with { Views = [] };
        Assert.True(Validate(proposal with { Sheets = [blank] }).IsValid);
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("one/two")]
    [InlineData("one\\two")]
    public void NewDestinationRejectsInvalidNames(string name) =>
        Assert.Contains(Validate(Proposal() with { NewDestinationFolderName = name }).Issues, i => i.Code == "folder.invalid_name");

    [Fact]
    public void NewDestinationIsValidatedWithoutCreatingFolder()
    {
        var snapshot = TestSnapshots.Create();
        var count = snapshot.Folders.Count;
        var spec = Proposal() with { NewDestinationFolderName = "AI drawing set" };
        Assert.True(DrawingSetSpecificationValidator.Validate(spec, snapshot).IsValid);
        Assert.Equal(count, snapshot.Folders.Count);
        Assert.NotEqual(DrawingSetPlanner.Digest(spec), DrawingSetPlanner.Digest(spec with { NewDestinationFolderName = "Other" }));
    }
    private static DrawingSetSpecification Proposal()
    {
        var doc = TestSnapshots.Create();
        return new(2, Guid.NewGuid(), doc.DocumentRuntimeSerialNumber, doc.Revision, doc.RootFolderId,
            [new("a01", "A01 Proposed", 420, 297,
                [new("site", "Site plan", "site_plan", 200, new(10, 18, 410, 277),
                    new(0, 0, 100), new(0, 0, 0), new(0, 1, 0), null) { HiddenLayerIds = [] }])]);
    }

    private static DrawingSetPreflight Validate(DrawingSetSpecification proposal) =>
        DrawingSetSpecificationValidator.Validate(proposal, TestSnapshots.Create());

    [Fact]
    public void VersionTwoRequiresExplicitVisibilityAndCaptionSpace()
    {
        var legacy = ChangeView(v => v with { HiddenLayerIds = null, BoundsMm = new(0, 0, 410, 287) });
        var sheet = legacy.Sheets[0];
        var view = sheet.Views[0];
        var invalid = Validate(legacy with { SchemaVersion = 2 });
        Assert.Contains(invalid.Issues, i => i.Code == "visibility.layers");
        Assert.Contains(invalid.Issues, i => i.Code == "frame.caption_space");
        var valid = legacy with { SchemaVersion = 2, Sheets = [sheet with { Views =
            [view with { BoundsMm = new(10, 18, 410, 277), HiddenLayerIds = [] }] }] };
        Assert.True(Validate(valid).IsValid);
        var parsed = DrawingSetSpecificationValidator.Parse(JsonSerializer.SerializeToElement(valid,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        Assert.True(Validate(parsed).IsValid);
    }

    [Fact]
    public void VersionTwoRejectsUnknownLayersAndCaptionBandOverlap()
    {
        var spec = Proposal();
        var sheet = spec.Sheets[0];
        var view = sheet.Views[0];
        var result = Validate(spec with { SchemaVersion = 2, Sheets = [sheet with { Views =
            [view with { BoundsMm = new(10, 18, 410, 130), HiddenLayerIds = [Guid.NewGuid()] },
             view with { Key = "second", BoundsMm = new(10, 135, 410, 277), HiddenLayerIds = [] }] }] });
        Assert.Contains(result.Issues, i => i.Code == "visibility.layers");
        Assert.Contains(result.Issues, i => i.Code == "caption.overlap");
        Assert.DoesNotContain(result.Issues, i => i.Code == "frame.overlap");
    }

    [Fact]
    public void VersionOneIsRejected() => Assert.Contains(
        Validate(Proposal() with { SchemaVersion = 1 }).Issues, i => i.Code == "version.unsupported");

    [Fact]
    public void ValidProposalReportsCountsWithoutMutatingSnapshot()
    {
        var snapshot = TestSnapshots.Create();
        var before = snapshot.Sheets.Count;
        var result = DrawingSetSpecificationValidator.Validate(Proposal(), snapshot);
        Assert.True(result.IsValid);
        Assert.Equal(1, result.SheetCount);
        Assert.Equal(1, result.ViewCount);
        Assert.Equal(before, snapshot.Sheets.Count);
    }

    [Fact]
    public void JsonRoundTripUsesStrictVersionedContract()
    {
        var proposal = Proposal();
        var json = JsonSerializer.SerializeToElement(proposal, new JsonSerializerOptions
        { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.True(Validate(DrawingSetSpecificationValidator.Parse(json)).IsValid);
    }

    [Fact]
    public void MissingVisibilityFieldIsRejectedDuringParsing()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Proposal(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }))!;
        json["sheets"]![0]!["views"]![0]!.AsObject().Remove("hidden_layer_ids");
        Assert.Throws<JsonException>(() => DrawingSetSpecificationValidator.Parse(
            JsonSerializer.SerializeToElement(json)));
    }

    [Theory]
    [InlineData("{\"schema_version\":1,\"unexpected\":true}")]
    [InlineData("{\"schema_version\":1,\"schema_version\":2}")]
    [InlineData("null")]
    [InlineData("{}")]
    public void RejectsAmbiguousOrUnknownJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<JsonException>(() => DrawingSetSpecificationValidator.Parse(document.RootElement));
    }

    [Fact]
    public void RejectsStaleDocumentAndUnknownDestination()
    {
        var result = Validate(Proposal() with { SourceRevision = -1, DocumentRuntimeSerialNumber = uint.MaxValue,
            DestinationFolderId = Guid.NewGuid(), SchemaVersion = 99, ProposalId = Guid.Empty });
        Assert.Equal(5, result.Issues.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(10001)]
    public void RejectsInvalidScale(double scale) => Assert.Contains(
        Validate(ChangeView(v => v with { ScaleDenominator = scale })).Issues, i => i.Code == "scale.invalid");

    [Fact]
    public void RejectsDegenerateCamera() => Assert.Contains(
        Validate(ChangeView(v => v with { CameraUp = new(0, 0, 1) })).Issues, i => i.Code == "camera.invalid");

    [Theory]
    [InlineData("floor_plan")]
    [InlineData("section")]
    public void CutDrawingsRequireAgentSpecifiedCuts(string kind) => Assert.Contains(
        Validate(ChangeView(v => v with { Kind = kind })).Issues, i => i.Code == "cut.required");

    [Fact]
    public void RejectsOffPageFrame() => Assert.Contains(
        Validate(ChangeView(v => v with { BoundsMm = new(-1, 0, 450, 200) })).Issues, i => i.Code == "frame.invalid");

    [Fact]
    public void RejectsDuplicateKeysAndOverlappingFrames()
    {
        var proposal = Proposal();
        var sheet = proposal.Sheets[0];
        var result = Validate(proposal with { Sheets = [sheet with { Views = [sheet.Views[0], sheet.Views[0]] }] });
        Assert.Contains(result.Issues, i => i.Code == "key.invalid");
        Assert.Contains(result.Issues, i => i.Code == "frame.overlap");
    }

    [Fact]
    public void NullCollectionsReturnActionableIssuesInsteadOfThrowing()
    {
        Assert.Contains(Validate(Proposal() with { Sheets = null! }).Issues, i => i.Code == "sheets.count");
        Assert.Contains(Validate(ChangeView(v => v with { BoundsMm = null! })).Issues, i => i.Code == "frame.invalid");
    }

    private static DrawingSetSpecification ChangeView(Func<DrawingViewSpecification, DrawingViewSpecification> change)
    {
        var proposal = Proposal();
        var sheet = proposal.Sheets[0];
        return proposal with { Sheets = [sheet with { Views = [change(sheet.Views[0])] }] };
    }
}
