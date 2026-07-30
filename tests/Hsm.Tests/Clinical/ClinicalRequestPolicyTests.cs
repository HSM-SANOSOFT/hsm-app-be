using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Application.Clinical.Commands.CreatePatient;
using Hsm.Application.Clinical.Queries.GetPatient;
using Hsm.Application.Clinical.Queries.SearchPatients;
using Hsm.Application.System.Queries.GetSystemStatus;
using Hsm.Domain.Identity;

namespace Hsm.Tests.Clinical;

/// <summary>
/// Pins the clinical PHI roles gate (KTD11) directly on the request types.
///
/// The frozen <c>@FhirController</c> decorates every <c>/fhir/R4/Patient</c>
/// route with <c>@Roles(...CLINICAL_STAFF_ROLES)</c> — doctor, nurse,
/// technician, therapist, pharmacist — enforced by the frozen
/// <c>RolesGuard</c>, which ALSO grants any <c>admin</c> principal a blanket
/// pass regardless of the required-roles list
/// (<see cref="Hsm.Web.Auth.RequestAuth.RequireRoles"/> reproduces this).
/// <see cref="Hsm.Application.Abstractions.Behaviors.AuthorizationBehavior{TRequest,TResult}"/>
/// has NO such bypass — it checks only <see cref="RequireRoleAttribute.Roles"/>
/// membership. Task 8's review already proved this gap for the users module.
/// So the admin bypass has to be encoded as an explicit member of the role
/// list here, or an admin FHIR call would clear the (still-present, see
/// <c>FhirEndpoints.GateAsync</c>) edge gate and then 403 in the pipeline —
/// a contract-visible regression the moment Task 15 removes the edge gate.
/// <c>PatientFhirContractTests.Every_clinical_role_and_admin_pass_the_gate</c>
/// is the executable proof admin belongs in the list.
/// </summary>
public class ClinicalRequestPolicyTests
{
    private static readonly string[] ExpectedClinicalRoles =
    [
        Roles.Doctor,
        Roles.Nurse,
        Roles.Technician,
        Roles.Therapist,
        Roles.Pharmacist,
        Roles.Admin,
    ];

    public static TheoryData<Type> ClinicalStaffRequests => new(
        typeof(GetPatientQuery),
        typeof(SearchPatientsQuery),
        typeof(CreatePatientCommand));

    [Theory]
    [MemberData(nameof(ClinicalStaffRequests))]
    public void Every_clinical_request_requires_a_clinical_role_or_admin(Type request)
    {
        var attribute = request.GetCustomAttribute<RequireRoleAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(ExpectedClinicalRoles.Length, attribute!.Roles.Count);
        foreach (var role in ExpectedClinicalRoles)
        {
            Assert.Contains(role, attribute.Roles);
        }

        Assert.Null(request.GetCustomAttribute<AllowAnonymousRequestAttribute>());
    }

    [Fact]
    public void System_status_is_reachable_without_a_principal()
    {
        Assert.NotNull(typeof(GetSystemStatusQuery).GetCustomAttribute<AllowAnonymousRequestAttribute>());
        Assert.Null(typeof(GetSystemStatusQuery).GetCustomAttribute<RequireRoleAttribute>());
    }
}
