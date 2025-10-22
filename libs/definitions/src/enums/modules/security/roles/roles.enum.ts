export enum SystemRoles {
  Admin = 'admin',
  Integration = 'integration',
}

export enum ClinicalRoles {
  Doctor = 'doctor',
  Nurse = 'nurse',
}

export const Role = {
  System: SystemRoles,
  Clinical: ClinicalRoles,
} as const;
