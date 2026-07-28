using Hsm.Application.Coms;
using Hsm.Domain.Templates;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Coms;

/// <summary>
/// The U14 coms suite host (hsm_coms_test). Swaps the email transport for a
/// capturing double, seeds the mandrill webhook signing key (frozen
/// COMS_WEBHOOK_SIGNING_KEYS), and shortens the dispatcher's retry backoff so
/// failure paths settle within test time.
/// </summary>
public sealed class ComsApiFactory : ContractApiFactory
{
    public const string SigningKey = "coms-contract-test-signing-key";

    public CapturingEmailTransport Transport { get; } = new();

    protected override string DatabaseName => "hsm_coms_test";

    protected override void ConfigureModule(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "Settings:Seed:COMS_WEBHOOK_SIGNING_KEYS",
            $$"""{"mandrill":"{{SigningKey}}"}""");
        builder.UseSetting("Coms:RetryBaseDelayMs", "25");
        builder.ConfigureServices(services => services.AddSingleton<IEmailTransport>(Transport));
    }

    /// <summary>Seeds a BASE + EMAIL_INTERNAL template pair directly; returns the email template's name.</summary>
    public async Task<string> SeedEmailTemplateAsync(string? schemaJson = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var baseTemplate = new Template
        {
            Id = Guid.NewGuid(),
            Category = TemplateCategories.Base,
            Name = $"base_{Guid.NewGuid():N}",
            IsActive = true,
            SchemaJson = "{}",
            Content = "<html>{{{body}}}</html>",
        };
        var email = new Template
        {
            Id = Guid.NewGuid(),
            Category = TemplateCategories.EmailInternal,
            Name = $"email_{Guid.NewGuid():N}",
            IsActive = true,
            SchemaJson = schemaJson ?? """{"patientName":"string"}""",
            Content = "<p>Hola {{patientName}}</p>",
            BaseTemplateId = baseTemplate.Id,
            Email = new TemplateEmail
            {
                Subject = "Hola {{patientName}}",
                FromEmail = "no-reply@hsm.test",
                FromName = "HSM",
            },
        };
        email.Email!.Id = email.Id;
        db.Templates.AddRange(baseTemplate, email);
        await db.SaveChangesAsync();
        return email.Name;
    }
}

/// <summary>
/// Captures outbound emails; can be told to fail the next N sends so
/// dispatch-failure and resend paths are testable.
/// </summary>
public sealed class CapturingEmailTransport : IEmailTransport
{
    private readonly CapturingSink<OutboundEmail> _sent = new("Simulated SMTP failure");

    public IReadOnlyList<OutboundEmail> Sent => _sent.Snapshot();

    public void FailNext(int count) => _sent.FailNext(count);

    public Task<string> SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        _sent.Record(email);
        return Task.FromResult($"<{Guid.NewGuid():N}@contract.test>");
    }
}
