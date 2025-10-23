export enum SystemRoles {
  Admin = 'admin',
  Integration = 'integration',
}

export enum ClinicalRoles {
  Doctor = 'doctor',
  Nurse = 'nurse',
}

export enum AdministrativeRole {
  Receptionist = 'receptionist',

}

export enum DefaultRole {
  Basic = 'basic',
  User = 'user',
  Auditor = 'auditor',
}

export const Role = {
  Default: DefaultRole,
  System: SystemRoles,
  Clinical: ClinicalRoles,
  Administrative: AdministrativeRole,
} as const;
