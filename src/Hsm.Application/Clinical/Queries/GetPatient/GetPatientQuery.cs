using Hsm.Application.Abstractions;
using Hsm.Domain.Clinical;
using Hsm.Domain.Identity;

namespace Hsm.Application.Clinical.Queries.GetPatient;

/// <summary>
/// Frozen PatientService.getByIdAsFhir: read by logical id. Gated by the
/// frozen clinical PHI roles gate (KTD11, fhir-controller.base.ts):
/// CLINICAL_STAFF_ROLES — doctor, nurse, technician, therapist, pharmacist —
/// PLUS admin, because the frozen RolesGuard granted any admin principal a
/// blanket pass on top of that list and
/// <see cref="Hsm.Application.Abstractions.Behaviors.AuthorizationBehavior{TRequest,TResult}"/>
/// has no such bypass; admin has to be a literal member of this attribute's
/// role list or an admin FHIR caller 403s once the pipeline is the sole gate
/// (Task 15). See <c>ClinicalRequestPolicyTests</c> and
/// <c>PatientFhirContractTests.Every_clinical_role_and_admin_pass_the_gate</c>.
/// </summary>
[RequireRole(Roles.Doctor, Roles.Nurse, Roles.Technician, Roles.Therapist, Roles.Pharmacist, Roles.Admin)]
public sealed record GetPatientQuery(string Id) : IQuery<Patient>;
