namespace Hsm.Tests.Architecture;

/// <summary>
/// The application layer references roles, never products (plan U9): no ORM,
/// S3, search-client, or cache-client assembly may be referenced by
/// Hsm.Application — infrastructure types must not appear in its signatures,
/// which is impossible if the assemblies are not referenced at all.
/// </summary>
public class PortPurityTests
{
    [Fact]
    public void Application_references_no_store_product_assemblies()
    {
        var references = typeof(Application.Proving.IProvingRepository).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        var forbiddenPrefixes = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "AWSSDK",
            "Meilisearch",
            "StackExchange.Redis",
        };

        var violations = references
            .Where(r => forbiddenPrefixes.Any(p => r.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(violations);
    }
}
