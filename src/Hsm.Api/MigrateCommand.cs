using Hsm.Infrastructure;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Api;

/// <summary>
/// Schema migration as an explicit, separate invocation:
/// <c>dotnet run --project src/Hsm.Api -- --migrate</c>.
///
/// It is deliberately NOT part of normal boot. The deployment runs three
/// request-serving processes (api, web, worker) and any number of replicas of
/// each; migrating on startup means every one of them races
/// <c>Migrate()</c> against the same database on every rollout, which is a
/// known way to deadlock a deploy or half-apply a migration. So the migration
/// runs once, as its own step, and the hosts only ever open connections to a
/// schema someone already put there.
///
/// The DbContext is composed by <see cref="DependencyInjection.AddHsmInfrastructure"/>
/// — the same registration the hosts use, so migrations are applied through
/// the same <see cref="HsmDbContext"/> that serves requests; there is no
/// second, migration-only context that could drift from it. What is skipped is
/// everything else a host does: no Kestrel, no request pipeline, no hosted
/// services, and no Redis or S3 connection — this process talks to PostgreSQL
/// and exits.
/// </summary>
internal static partial class MigrateCommand
{
    private const string Flag = "--migrate";

    /// <summary>Whether this invocation is the migrate step rather than a host boot.</summary>
    internal static bool IsRequested(string[] args) =>
        args.Contains(Flag, StringComparer.Ordinal);

    /// <summary>
    /// Applies every pending migration and returns the process exit code.
    /// A failure surfaces as a non-zero exit so a deployment step stops on it
    /// instead of starting hosts against a schema that was never migrated.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args)
    {
        // The host builder, for its configuration sources only: the same
        // appsettings/environment/command-line chain the web host binds, so
        // the migrate step reads the connection string the hosts will read.
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddHsmInfrastructure(builder.Configuration);

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<HsmDbContext>>();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        if (pending.Length == 0)
        {
            AlreadyUpToDate(logger);
            return 0;
        }

        var names = string.Join(", ", pending);
        Applying(logger, pending.Length, names);
        await db.Database.MigrateAsync();
        Applied(logger);
        return 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database is up to date; no migrations to apply")]
    private static partial void AlreadyUpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migration(s): {Migrations}")]
    private static partial void Applying(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations applied")]
    private static partial void Applied(ILogger logger);
}
