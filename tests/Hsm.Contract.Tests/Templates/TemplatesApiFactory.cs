using Hsm.Domain.Templates;
using Hsm.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Contract.Tests.Templates;

/// <summary>The U14 template suite host (hsm_templates_test).</summary>
public sealed class TemplatesApiFactory : ContractApiFactory
{
    protected override string DatabaseName => "hsm_templates_test";

    /// <summary>Seeds a BASE template row directly and returns its id.</summary>
    public async Task<Guid> SeedBaseTemplateAsync(string name, string content = "<html>{{{body}}}</html>")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var template = new Template
        {
            Id = Guid.NewGuid(),
            Category = TemplateCategories.Base,
            Name = name,
            IsActive = true,
            SchemaJson = "{}",
            Content = content,
        };
        db.Templates.Add(template);
        await db.SaveChangesAsync();
        return template.Id;
    }
}
