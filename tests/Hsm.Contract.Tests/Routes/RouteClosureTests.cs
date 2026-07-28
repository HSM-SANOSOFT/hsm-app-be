using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Routes;

/// <summary>
/// The U16 route-closure gate: diffs the API surface the host actually maps
/// against the frozen contract snapshot
/// (docs/reference/2026-07-27-frozen-api-routes.txt, 58 operations). Every
/// snapshot operation must be either implemented or listed in
/// <see cref="DeliberateDrops"/> with its recorded reason — and nothing may
/// drift in the other direction (no unaccounted-for implemented API route,
/// no allowlisted operation that is silently implemented after all).
/// </summary>
[Trait("Infra", "true")]
public sealed class RouteClosureTests(RouteClosureTests.RoutesFactory factory)
    : IClassFixture<RouteClosureTests.RoutesFactory>
{
    public sealed class RoutesFactory : ContractApiFactory
    {
        protected override string DatabaseName => "hsm_routes_test";
    }

    /// <summary>
    /// Operations from the frozen snapshot that are deliberately NOT
    /// implemented in this release, each with its recorded reason. In the new
    /// application these paths return a plain 404 — a contract change for
    /// integration (A3) consumers of the frozen FHIR surface, recorded in the
    /// definition of done rather than silently missing.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DeliberateDrops =
        new Dictionary<string, string>
        {
            // FHIR R4 Encounter + ServiceRequest: the rewrite plan's Scope
            // Boundaries exclude clinical modules beyond the contract's
            // patient lookup (orders, encounters, LIS/RIS routing). Recorded
            // as a deliberate drop and follow-up in the definition of done,
            // capability C5 (docs/plans/2026-07-27-002-minor-release-
            // definition-of-done.md); the shared FHIR mechanics (raw-resource
            // responses, OperationOutcome errors, clinical roles gate) landed
            // with the Patient surface, so these resources rebuild against
            // that seam when their modules arrive.
            ["GET /fhir/R4/Encounter"] = "DoD C5 deliberate drop — clinical modules beyond patient lookup are out of the minor",
            ["POST /fhir/R4/Encounter"] = "DoD C5 deliberate drop — clinical modules beyond patient lookup are out of the minor",
            ["GET /fhir/R4/Encounter/{id}"] = "DoD C5 deliberate drop — clinical modules beyond patient lookup are out of the minor",
            ["GET /fhir/R4/ServiceRequest"] = "DoD C5 deliberate drop — clinical modules beyond patient lookup are out of the minor",
            ["POST /fhir/R4/ServiceRequest"] = "DoD C5 deliberate drop — clinical modules beyond patient lookup are out of the minor",
            ["GET /fhir/R4/ServiceRequest/{id}"] = "DoD C5 deliberate drop — clinical modules beyond patient lookup are out of the minor",
        };

    private static HashSet<string> NormalizedDrops() =>
        DeliberateDrops.Keys.Select(Normalize).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_snapshot_operation_is_implemented_or_a_recorded_drop()
    {
        var snapshot = SnapshotOperations();
        var implemented = ImplementedOperations();
        var drops = NormalizedDrops();

        var missing = snapshot
            .Where(op => !implemented.Contains(op) && !drops.Contains(op))
            .ToList();
        Assert.True(
            missing.Count == 0,
            "Frozen operations neither implemented nor recorded as dropped:\n" + string.Join('\n', missing));

        var dropsThatExist = NormalizedDrops().Where(implemented.Contains).ToList();
        Assert.True(
            dropsThatExist.Count == 0,
            "Allowlisted drops that are actually implemented (remove them from the allowlist and the DoD):\n"
            + string.Join('\n', dropsThatExist));
    }

    [Fact]
    public void No_implemented_api_route_is_outside_the_snapshot()
    {
        var snapshot = SnapshotOperations();
        var extras = ImplementedOperations().Where(op => !snapshot.Contains(op)).ToList();

        Assert.True(
            extras.Count == 0,
            "Implemented API operations not present in the frozen snapshot:\n" + string.Join('\n', extras));
    }

    [Fact]
    public void Every_recorded_drop_still_exists_in_the_snapshot()
    {
        var snapshot = SnapshotOperations();
        var stale = NormalizedDrops().Where(op => !snapshot.Contains(op)).ToList();

        Assert.True(
            stale.Count == 0,
            "Allowlist entries that no longer match a snapshot operation:\n" + string.Join('\n', stale));
    }

    /// <summary>The frozen snapshot, normalized to 'METHOD /path' with bare {param} names.</summary>
    private static HashSet<string> SnapshotOperations()
    {
        var path = Path.Combine(RepoRoot(), "docs", "reference", "2026-07-27-frozen-api-routes.txt");
        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(Normalize)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every routable endpoint the host maps under the API surface (/v1 or
    /// /fhir) — the Blazor/static surface is not part of the frozen contract.
    /// </summary>
    private HashSet<string> ImplementedOperations()
    {
        var operations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in factory.Services.GetServices<EndpointDataSource>())
        {
            foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
            {
                var pattern = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
                if (!pattern.StartsWith("/v1/", StringComparison.Ordinal)
                    && pattern != "/v1"
                    && !pattern.StartsWith("/fhir/", StringComparison.Ordinal))
                {
                    continue;
                }

                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
                foreach (var method in methods)
                {
                    operations.Add(Normalize($"{method} {pattern}"));
                }
            }
        }

        return operations;
    }

    /// <summary>
    /// 'METHOD /path' with every route parameter reduced to a bare {} token so
    /// parameter names and constraints never mask a real difference.
    /// </summary>
    private static string Normalize(string operation)
    {
        var parts = operation.Split(' ', 2, StringSplitOptions.TrimEntries);
        var path = parts[1].TrimEnd('/');
        var segments = path
            .Split('/')
            .Select(segment => segment.StartsWith('{') && segment.EndsWith('}') ? "{}" : segment);
        return $"{parts[0].ToUpperInvariant()} {string.Join('/', segments)}";
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hsm.sln")))
        {
            dir = dir.Parent!;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Hsm.sln not found above the test assembly.");
    }
}
