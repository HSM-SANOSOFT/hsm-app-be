using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Hsm.Tests.Architecture;

/// <summary>
/// Option B: screens live inside Hsm.Web, which references Infrastructure for
/// DI — so the compiler can no longer make a shortcut impossible (it did while
/// screens lived in their own project). These two tests are the replacement.
/// Source scanning catches the realistic violation (@inject / @using, including
/// a global smuggled through _Imports.razor); reflection catches anything that
/// survives a folder rename.
/// </summary>
public class ScreenIsolationTests
{
    private static readonly string[] Forbidden =
    [
        "Hsm.Application", "Hsm.Infrastructure", "Microsoft.EntityFrameworkCore",
    ];

    [Fact]
    public void No_razor_file_references_a_forbidden_namespace()
    {
        var screens = Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), "src", "Hsm.Web"), "*.razor", SearchOption.AllDirectories);

        var violations = new List<string>();
        foreach (var file in screens)
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("@using", StringComparison.Ordinal)
                    && !trimmed.StartsWith("@inject", StringComparison.Ordinal))
                {
                    continue;
                }

                if (Forbidden.Any(f => trimmed.Contains(f, StringComparison.Ordinal)))
                {
                    violations.Add($"{Path.GetFileName(file)}: {trimmed}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void No_component_injects_a_forbidden_type()
    {
        var componentTypes = typeof(Hsm.Web.Routes).Assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract);

        var violations = componentTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public
                                             | BindingFlags.NonPublic))
            .Where(p => p.GetCustomAttribute<InjectAttribute>() is not null)
            .Where(p => Forbidden.Any(f =>
                (p.PropertyType.Namespace ?? string.Empty).StartsWith(f, StringComparison.Ordinal)))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}: {p.PropertyType.Name}")
            .ToList();

        Assert.Empty(violations);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hsm.sln")))
        {
            dir = dir.Parent!;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Hsm.sln not found.");
    }
}
