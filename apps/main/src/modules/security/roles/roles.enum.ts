enum SystemRole {
  Admin = 'admin',
  Integration = 'integration',
}

enum ClinicalRole {
  Doctor = 'doctor',
  Nurse = 'nurse',
}

export const Role = {
  System: SystemRole,
  Clinical: ClinicalRole,
};
