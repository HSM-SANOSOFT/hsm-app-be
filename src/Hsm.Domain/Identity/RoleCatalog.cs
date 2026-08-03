namespace Hsm.Domain.Identity;

/// <summary>
/// Role identifiers frozen by the reference implementation
/// (packages/common/src/enums/roles.enum.ts at freeze/typescript-2026-07-27).
/// The string values are contract: they ride in JWTs and role rows.
/// </summary>
public static class Roles
{
    public const string Admin = "admin";
    public const string Developer = "developer";
    public const string Integration = "integration";
    public const string Auditor = "auditor";

    public const string Doctor = "doctor";
    public const string Nurse = "nurse";
    public const string Technician = "technician";
    public const string Therapist = "therapist";
    public const string Pharmacist = "pharmacist";

    public const string Admission = "admission";
    public const string Billing = "billing";
    public const string Scheduling = "scheduling";
    public const string HumanResources = "human_resources";

    public const string Maintenance = "maintenance";
    public const string Housekeeping = "housekeeping";
    public const string Security = "security";
    public const string It = "it";

    public const string Accountant = "accountant";
    public const string Payroll = "payroll";
    public const string FinancialAnalyst = "financial_analyst";
    public const string InsuranceSpecialist = "insurance_specialist";

    public const string CommunityManager = "community_manager";
    public const string Designer = "designer";
    public const string CrmSpecialist = "crm_specialist";

    public const string QualityOfficer = "quality_officer";
    public const string ComplianceOfficer = "compliance_officer";
    public const string ProcessAnalyst = "process_analyst";

    public const string LegalCounsel = "legal_counsel";
    public const string Paralegal = "paralegal";

    public const string ClinicalResearcher = "clinical_researcher";
    public const string ResearchCoordinator = "research_coordinator";
    public const string DataAnalyst = "data_analyst";

    public const string SocialWorker = "social_worker";
    public const string CaseManager = "case_manager";
    public const string PatientAdvocate = "patient_advocate";

    public const string GuestRelations = "guest_relations";
    public const string PatientServices = "patient_services";

    public const string Patient = "patient";
    public const string Family = "family";
}

/// <summary>
/// Maps every role to its domain branch — the frozen RolesService resolved
/// role→domain pairs before persisting role rows; unknown roles resolve to
/// nothing (frozen behavior: silently skipped, or rejected by the caller).
/// </summary>
public static class RoleCatalog
{
    // Declaration order matters: OrderedRoles must exist before BuildCatalog
    // populates it.
    private static readonly List<string> OrderedRoles = [];
    private static readonly Dictionary<string, string> DomainByRole = BuildCatalog();

    private static Dictionary<string, string> BuildCatalog()
    {
        var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
        void AddBranch(string domain, params string[] roles)
        {
            foreach (var role in roles)
            {
                catalog[role] = domain;
                OrderedRoles.Add(role);
            }
        }

        AddBranch("System", Roles.Admin, Roles.Developer, Roles.Integration, Roles.Auditor);
        AddBranch("Clinical", Roles.Doctor, Roles.Nurse, Roles.Technician, Roles.Therapist, Roles.Pharmacist);
        AddBranch("Administrative", Roles.Admission, Roles.Billing, Roles.Scheduling, Roles.HumanResources);
        AddBranch("Operational", Roles.Maintenance, Roles.Housekeeping, Roles.Security, Roles.It);
        AddBranch("Finance", Roles.Accountant, Roles.Payroll, Roles.FinancialAnalyst, Roles.InsuranceSpecialist);
        AddBranch("Marketing", Roles.CommunityManager, Roles.Designer, Roles.CrmSpecialist);
        AddBranch("Quality", Roles.QualityOfficer, Roles.ComplianceOfficer, Roles.ProcessAnalyst);
        AddBranch("Legal", Roles.LegalCounsel, Roles.Paralegal);
        AddBranch("Research", Roles.ClinicalResearcher, Roles.ResearchCoordinator, Roles.DataAnalyst);
        AddBranch("SocialWork", Roles.SocialWorker, Roles.CaseManager, Roles.PatientAdvocate);
        AddBranch("Hospitality", Roles.GuestRelations, Roles.PatientServices);
        AddBranch("Patient", Roles.Patient, Roles.Family);
        return catalog;
    }

    /// <summary>
    /// All known role identifiers in the frozen declaration order — the order
    /// of the frozen ROLE_VALUES constant, which rides in validation messages.
    /// </summary>
    public static IReadOnlyList<string> All => OrderedRoles;

    public static bool IsKnown(string role) => DomainByRole.ContainsKey(role);

    /// <summary>
    /// Whether a role may be handed to a staff account. The frozen
    /// createStaffUser guard rejects the patient-facing branch outright: those
    /// accounts are created by the patient signup path, never by an admin
    /// provisioning staff.
    /// </summary>
    public static bool IsAssignableToStaff(string role) =>
        role is not (Roles.Patient or Roles.Family);

    /// <summary>The domain branch for a role, or null when unknown.</summary>
    public static string? DomainOf(string role) =>
        DomainByRole.TryGetValue(role, out var domain) ? domain : null;
}
