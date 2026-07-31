namespace Hsm.Tests.Architecture;

/// <summary>
/// Contracts is the client-isolation boundary's only shared surface (rewrite
/// plan U8): it must stay a leaf assembly with no dependency back into any
/// other Hsm.* project, or a screen (or anything else) could smuggle a
/// forbidden type in through it. Recovered from the deleted
/// tests/Hsm.Architecture.Tests/ClientIsolationBoundaryTests.cs (Task 16
/// review, finding: the fold to Hsm.Web dropped this invariant with no
/// replacement) — same assertion, re-homed here since it protects Contracts
/// itself rather than anything screen-specific.
/// </summary>
public class ContractsPurityTests
{
    [Fact]
    public void Contracts_assembly_depends_on_no_other_hsm_assembly()
    {
        // Contracts is the boundary's only shared surface; it must stay a leaf.
        var direct = typeof(Contracts.Ui.ISystemStatusUiService).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Hsm.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(direct);
    }
}
