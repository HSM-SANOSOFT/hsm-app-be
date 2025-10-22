import { Injectable } from '@nestjs/common';

import { Role } from '@hsm-lib/definitions/enums/modules/security/roles';

import type { IUser } from '@hsm-lib/definitions/interfaces';

@Injectable()
export class UsersService {
  private readonly users: IUser[] = [
    {
      id: '1',
      username: 'john_doe',
      email: 'john.doe@example.com',
      password: 'password123',
      name: 'John',
      surname: 'Doe',
      roles: [Role.System.Admin],
    },
    {
      id: '2',
      username: 'jane_smith',
      email: 'jane.smith@example.com',
      password: 'password456',
      name: 'Jane',
      surname: 'Smith',
      roles: [Role.Clinical.Doctor],
    },
  ];

  async findOne(username: string): Promise<IUser | undefined> {
    return this.users.find(user => user.username === username);
  }
}
