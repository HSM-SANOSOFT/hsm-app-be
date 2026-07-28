using System.Reflection;
using System.Xml.Linq;

namespace Hsm.Architecture.Tests;

/// <summary>
/// The client-isolation boundary (rewrite plan U8): the component library may
/// see shared contracts and nothing else. The project reference graph already
/// makes a violation a compile error — these tests state the intent for a
/// future reader tempted to "just add one reference", and catch indirect
/// paths a package could open.
/// </summary>
public class ClientIsolationBoundaryTests
{
    private static readonly string[] ForbiddenFromComponents =
    [
        "Hsm.Application",
        "Hsm.Infrastructure",
        "Hsm.Domain",
        "Hsm.Web",
        "Hsm.Worker",
    ];

    [Fact]
    public void Components_project_references_only_contracts()
    {
        var csproj = XDocument.Load(
            Path.Combine(RepoRoot(), "src", "Hsm.Web.Components", "Hsm.Web.Components.csproj"));

        var references = csproj
            .Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(
                ((string?)r.Attribute("Include"))?.Replace('\\', '/')))
            .ToList();

        Assert.Equal(["Hsm.Contracts"], references);
    }

    [Fact]
    public void Components_assembly_has_no_path_to_application_or_infrastructure()
    {
        // Transitive closure over the compiled assembly's references, so a
        // shared package smuggling in a forbidden assembly is caught even
        // though the csproj looks clean.
        var closure = ReferenceClosure(typeof(Web.Components.SystemStatusPanel).Assembly);

        var violations = closure
            .Where(name => ForbiddenFromComponents.Contains(name))
            .ToList();

        Assert.Empty(violations);
    }

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

    private static HashSet<string> ReferenceClosure(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<AssemblyName>(root.GetReferencedAssemblies());
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (name.Name is null || !seen.Add(name.Name))
            {
                continue;
            }

            // Only walk into assemblies we can actually load from the test
            // context; framework assemblies outside Hsm.* cannot reach back
            // into Hsm.* project assemblies, so failures to load are safe to
            // skip for boundary purposes — except Hsm.* itself, which always
            // loads because the test project references the whole graph.
            try
            {
                foreach (var child in Assembly.Load(name).GetReferencedAssemblies())
                {
                    queue.Enqueue(child);
                }
            }
            catch (FileNotFoundException)
            {
                // Not resolvable in the test context; cannot be an Hsm project assembly.
            }
        }

        return seen;
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
