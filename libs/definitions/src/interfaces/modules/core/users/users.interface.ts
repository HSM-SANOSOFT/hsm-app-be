import type { Roles } from '@hsm-lib/definitions/types/modules/security/roles';

export interface IUser {
  id: string;
  username: string;
  email: string;
  password: string;
  name: string;
  surname: string;
  roles: Roles[];
}
