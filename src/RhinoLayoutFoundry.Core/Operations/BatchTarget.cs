using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record BatchTarget(
    OverviewNodeKey Key,
    string Label,
    bool Included = true,
    int DetailCount = 0,
    double PageWidth = 0,
    double PageHeight = 0,
    string PageUnitSystem = "",
    string DisplayModeSummary = "—",
    string TitleBlockSummary = "—");

