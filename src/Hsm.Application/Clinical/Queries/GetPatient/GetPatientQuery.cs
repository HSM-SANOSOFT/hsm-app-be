using Hsm.Application.Abstractions;
using Hsm.Domain.Clinical;
using Hsm.Domain.Identity;

namespace Hsm.Application.Clinical.Queries.GetPatient;

/// <summary>
/// Read by logical id. Gated by the clinical PHI roles gate:
/// CLINICAL_STAFF_ROLES — doctor, nurse, technician, therapist, pharmacist —
/// PLUS admin. Admin has to be a literal member of this attribute's role list
/// because
/// <see cref="Hsm.Application.Abstractions.Behaviors.AuthorizationBehavior{TRequest,TResult}"/>
/// grants no automatic bypass for admin principals; an admin FHIR caller
/// would otherwise 403 once the pipeline is the sole gate (Task 15). See
/// <c>ClinicalRequestPolicyTests</c> and
/// <c>PatientFhirContractTests.Every_clinical_role_and_admin_pass_the_gate</c>.
/// </summary>
[RequireRole(Roles.Doctor, Roles.Nurse, Roles.Technician, Roles.Therapist, Roles.Pharmacist, Roles.Admin)]
public sealed record GetPatientQuery(string Id) : IQuery<Patient>;
