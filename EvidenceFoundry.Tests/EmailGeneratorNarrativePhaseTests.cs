using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class EmailGeneratorNarrativePhaseTests
{
    [Fact]
    public void GetNarrativePhaseSingleBatchEmphasizesUnresolvedOutcome()
    {
        var phase = EmailGenerator.GetNarrativePhase(0, 1);

        Assert.Contains("SINGLE-BATCH", phase, StringComparison.Ordinal);
        Assert.Contains("unresolved", phase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetNarrativePhaseFirstBatchUsesBeginning()
    {
        var phase = EmailGenerator.GetNarrativePhase(0, 3);

        Assert.StartsWith("BEGINNING", phase, StringComparison.Ordinal);
    }

    [Fact]
    public void GetNarrativePhaseMiddleBatchUsesMiddle()
    {
        var phase = EmailGenerator.GetNarrativePhase(1, 3);

        Assert.StartsWith("MIDDLE (Part 2 of 3)", phase, StringComparison.Ordinal);
    }

    [Fact]
    public void GetNarrativePhaseLastBatchAvoidsConclusion()
    {
        var phase = EmailGenerator.GetNarrativePhase(2, 3);

        Assert.StartsWith("LATE STAGE", phase, StringComparison.Ordinal);
        Assert.DoesNotContain("CONCLUSION", phase, StringComparison.OrdinalIgnoreCase);
    }
}
