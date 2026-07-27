using Hsm.Application.Templates;
using Hsm.Domain.Templates;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Templates;

/// <summary>EF Core adapter for <see cref="ITemplateStore"/>.</summary>
public sealed class TemplateStore(HsmDbContext db) : ITemplateStore
{
    public async Task<Template?> FindByIdAsync(
        Guid id, bool withChildren = false, bool withBase = false, CancellationToken ct = default) =>
        await Query(withChildren, withBase).FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Template?> FindByIdentifierAsync(
        string identifier, bool withChildren = false, bool withBase = false, CancellationToken ct = default)
    {
        var query = Query(withChildren, withBase);
        // The frozen id-or-name rule: a UUID-shaped identifier matches id OR name.
        if (Guid.TryParse(identifier, out var id))
        {
            return await query.FirstOrDefaultAsync(t => t.Id == id || t.Name == identifier, ct);
        }

        return await query.FirstOrDefaultAsync(t => t.Name == identifier, ct);
    }

    public async Task<IReadOnlyList<Template>> ListAsync(string? category, CancellationToken ct = default)
    {
        var query = Query(withChildren: true, withBase: false);
        if (category is not null)
        {
            query = query.Where(t => t.Category == category);
        }

        return await query.OrderBy(t => t.Name).ToListAsync(ct);
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken ct = default) =>
        db.Templates.AnyAsync(t => t.Name == name && (excludeId == null || t.Id != excludeId), ct);

    public Task<int> CountReferencingBaseAsync(Guid id, CancellationToken ct = default) =>
        db.Templates.CountAsync(t => t.BaseTemplateId == id, ct);

    public async Task AddAsync(Template entity, CancellationToken ct = default) =>
        await db.Templates.AddAsync(entity, ct);

    public Task RemoveAsync(Template entity, CancellationToken ct = default)
    {
        // Loaded child shape rows delete with the parent (shared-PK cascade).
        db.Templates.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task AddParseLogAsync(TemplateParseLog log, CancellationToken ct = default) =>
        await db.TemplateParseLogs.AddAsync(log, ct);

    private IQueryable<Template> Query(bool withChildren, bool withBase)
    {
        IQueryable<Template> query = db.Templates;
        if (withChildren)
        {
            query = query.Include(t => t.Email).Include(t => t.Sms).Include(t => t.Doc);
        }

        if (withBase)
        {
            query = query.Include(t => t.BaseTemplate);
        }

        return query;
    }
}
