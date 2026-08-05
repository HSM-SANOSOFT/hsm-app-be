using Hsm.Application.Abstractions;
using Hsm.Domain.Clinical;
using Hsm.Domain.Identity;

namespace Hsm.Application.Clinical.Queries.SearchPatients;

/// <summary>
/// Match value always, system when
/// the token carried one. Same clinical-staff-plus-admin gate as
/// <see cref="GetPatient.GetPatientQuery"/> — see its XML doc for why admin is
/// a literal member of the role list rather than relying on a pipeline
/// bypass that does not exist.
/// </summary>
[RequireRole(Roles.Doctor, Roles.Nurse, Roles.Technician, Roles.Therapist, Roles.Pharmacist, Roles.Admin)]
public sealed record SearchPatientsQuery(string? System, string Value) : IQuery<IReadOnlyList<Patient>>;
