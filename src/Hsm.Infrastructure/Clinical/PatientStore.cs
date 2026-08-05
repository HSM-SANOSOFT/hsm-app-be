using Hsm.Application.Clinical;
using Hsm.Domain.Clinical;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Clinical;

/// <summary>
/// EF Core adapter for the patient port. Identifier rows auto-include
/// (identifier rows always travel with the patient), and every search
/// parameter binds — the unique (system, value) index is the database
/// backstop behind the handler's 409.
/// </summary>
public sealed class PatientStore(HsmDbContext db) : IPatientStore
{
    public Task<Patient?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Patient>> SearchByIdentifierAsync(
        string? system, string value, CancellationToken ct = default)
    {
        var query = db.Patients.AsNoTracking().Where(p => p.Identifiers.Any(
            i => i.Value == value && (system == null || i.System == system)));
        return await query.OrderBy(p => p.CreatedAt).ToListAsync(ct);
    }

    public async Task<bool> AnyIdentifierExistsAsync(
        IReadOnlyList<(string System, string Value)> identifiers, CancellationToken ct = default)
    {
        if (identifiers.Count == 0)
        {
            return false;
        }

        // One query: candidate rows matching any requested system AND any
        // requested value, then the exact pair check in memory.
        var systems = identifiers.Select(i => i.System).Distinct().ToList();
        var values = identifiers.Select(i => i.Value).Distinct().ToList();
        var candidates = await db.PatientIdentifiers.AsNoTracking()
            .Where(i => systems.Contains(i.System) && values.Contains(i.Value))
            .Select(i => new { i.System, i.Value })
            .ToListAsync(ct);

        var wanted = identifiers.ToHashSet();
        return candidates.Any(c => wanted.Contains((c.System, c.Value)));
    }

    public async Task AddAsync(Patient patient, CancellationToken ct = default)
    {
        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);
    }
}
