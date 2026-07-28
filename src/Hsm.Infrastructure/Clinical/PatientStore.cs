using Hsm.Application.Clinical;
using Hsm.Domain.Clinical;
using Hsm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Clinical;

/// <summary>
/// EF Core adapter for the patient port. Identifier rows auto-include (the
/// frozen entity was eager), and every search parameter binds — the unique
/// (system, value) index is the database backstop behind the handler's 409.
/// </summary>
public sealed class PatientStore(HsmDbContext db) : IPatientStore
{
    public Task<Patient?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Patient>> SearchByIdentifierAsync(
        string? system, string value, CancellationToken ct = default)
    {
        var query = db.Patients.Where(p => p.Identifiers.Any(
            i => i.Value == value && (system == null || i.System == system)));
        return await query.OrderBy(p => p.CreatedAt).ToListAsync(ct);
    }

    public async Task<bool> AnyIdentifierExistsAsync(
        IReadOnlyList<(string System, string Value)> identifiers, CancellationToken ct = default)
    {
        foreach (var (system, value) in identifiers)
        {
            if (await db.PatientIdentifiers.AnyAsync(i => i.System == system && i.Value == value, ct))
            {
                return true;
            }
        }

        return false;
    }

    public async Task AddAsync(Patient patient, CancellationToken ct = default)
    {
        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);
    }
}
