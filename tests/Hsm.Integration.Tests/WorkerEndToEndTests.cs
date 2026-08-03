using Hsm.Application.Abstractions;
using Hsm.Application.Coms;
using Hsm.Application.Coms.Commands.DispatchEmailBatch;
using Hsm.Application.Ports;
using Hsm.Domain.Coms;
using Hsm.Domain.Templates;
using Hsm.Infrastructure.Persistence;
using Hsm.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hsm.Integration.Tests;

/// <summary>
/// The whole point of Task 20, proven end to end: a job enqueued into the
/// SHARED queue namespace is picked up and completed by the worker's own
/// composition — <c>AddHsmInfrastructure</c> + <c>AddHsmPipeline</c> +
/// <see cref="JobConsumerService"/>, with <c>AmbientPrincipal</c> as its
/// <c>ICurrentPrincipal</c> — with no HTTP host running at all.
///
/// <para>Before this, background work happened inside <c>Hsm.Api</c> on a queue
/// namespaced to that host instance: a restarted API stranded its own jobs and
/// no other process could ever see them. This test is what that arrangement
/// could not have passed.</para>
/// </summary>
public sealed class WorkerEndToEndTests : IAsyncLifetime
{
    private readonly CapturingTransport _transport = new();
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _provider = BuildWorker(_transport);
        await TestServices.RecreateAsync(_provider);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Worker_consumes_a_queued_email_batch_with_no_http_host()
    {
        var templateId = await SeedEmailTemplateAsync();
        var batchId = await SeedBatchAsync(templateId, "ada@worker.test");

        // Enqueued exactly as an HTTP request would enqueue it: through the
        // real IJobQueue, on the real coms queue, carrying the caller's actor.
        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AmbientPrincipal>().Set(
                new RequestActor(Guid.NewGuid().ToString(), ["doctor"], OnboardingCompleted: true));
            await scope.ServiceProvider.GetRequiredService<IJobQueue>()
                .EnqueueAsync(new DispatchEmailBatchCommand(batchId, null));
        }

        await RunConsumerUntilAsync(async () => await BatchStatusAsync(batchId) == EmailBatchStatus.Sent);

        Assert.Equal(EmailBatchStatus.Sent, await BatchStatusAsync(batchId));
        var sent = Assert.Single(_transport.Sent);
        Assert.Equal(["ada@worker.test"], sent.To);
        Assert.Contains("Ada", sent.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs the worker's hosted services — the consume loop and the delayed
    /// pump, exactly as <c>Hsm.Worker</c> runs them — until
    /// <paramref name="done"/> holds, then shuts them down.
    /// </summary>
    private async Task RunConsumerUntilAsync(Func<Task<bool>> done)
    {
        var hosted = _provider.GetServices<IHostedService>().ToList();
        Assert.Contains(hosted, service => service is JobConsumerService);

        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }

        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await done())
                {
                    return;
                }

                await Task.Delay(50);
            }

            Assert.Fail("Timed out waiting for the worker to complete the queued job.");
        }
        finally
        {
            foreach (var service in hosted)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }

    private async Task<string> BatchStatusAsync(Guid batchId)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        return await db.EmailBatches
            .AsNoTracking()
            .Where(b => b.Id == batchId)
            .Select(b => b.OverallStatus)
            .SingleAsync();
    }

    private async Task<Guid> SeedEmailTemplateAsync()
    {
        using var scope = _provider.CreateScope();
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
            SchemaJson = """{"patientName":"string"}""",
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
        return email.Id;
    }

    private async Task<Guid> SeedBatchAsync(Guid templateId, string toEmail)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var batch = new EmailBatch
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            FromEmail = "no-reply@hsm.test",
            DataJson = """{"patientName":"Ada"}""",
            OverallStatus = EmailBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        batch.Recipients.Add(new EmailRecipient
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            ToEmail = toEmail,
            Status = EmailRecipientStatus.Pending,
        });
        db.EmailBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch.Id;
    }

    /// <summary>
    /// The worker host's composition, minus the OpenTelemetry exporter: the
    /// same three calls <c>Hsm.Worker/Program.cs</c> makes, against a database
    /// and a queue namespace dedicated to this suite.
    /// </summary>
    private static ServiceProvider BuildWorker(IEmailTransport transport) =>
        TestServices.Build(
            builder => builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HsmDb"] = DedicatedDatabase("hsm_worker_e2e_test"),
                ["Jobs:KeyPrefix"] = $"hsmtest:{Guid.NewGuid():N}",
                ["Coms:RetryBaseDelayMs"] = "50",
            }),
            services =>
            {
                services.AddHsmPipeline();
                services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<AmbientPrincipal>());
                services.AddHsmJobProcessing();
                services.AddSingleton(transport);
            });

    private static string DedicatedDatabase(string name) => string.Join(
        ';',
        TestServices.Configuration["ConnectionStrings:HsmDb"]!
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
            .Append($"Database={name}"));

    /// <summary>Records what the worker actually sent, in place of SMTP.</summary>
    private sealed class CapturingTransport : IEmailTransport
    {
        private readonly List<OutboundEmail> _sent = [];

        public IReadOnlyList<OutboundEmail> Sent
        {
            get
            {
                lock (_sent)
                {
                    return [.. _sent];
                }
            }
        }

        public Task<string> SendAsync(OutboundEmail email, CancellationToken ct = default)
        {
            lock (_sent)
            {
                _sent.Add(email);
            }

            return Task.FromResult($"<{Guid.NewGuid():N}@worker.test>");
        }
    }
}
