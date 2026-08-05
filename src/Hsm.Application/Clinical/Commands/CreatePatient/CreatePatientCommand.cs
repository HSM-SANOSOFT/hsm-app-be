using Hsm.Application.Abstractions;
using Hsm.Domain.Clinical;
using Hsm.Domain.Identity;

namespace Hsm.Application.Clinical.Commands.CreatePatient;

/// <summary>
/// Persist a validated inbound FHIR Patient.
/// Same clinical-staff-plus-admin gate as
/// <see cref="Queries.GetPatient.GetPatientQuery"/> — see its XML doc for why
/// admin is a literal member of the role list rather than relying on a
/// pipeline bypass that does not exist.
/// </summary>
[RequireRole(Roles.Doctor, Roles.Nurse, Roles.Technician, Roles.Therapist, Roles.Pharmacist, Roles.Admin)]
public sealed record CreatePatientCommand(CreatePatientInput Input) : ICommand<Patient>;
