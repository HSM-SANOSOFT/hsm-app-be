import { Injectable } from '@nestjs/common';
import { InjectRepository } from '@nestjs/typeorm';
import { Repository } from 'typeorm/repository/Repository.js';

import { UserEntity } from '@hsm-lib/database/entities';
import { Role } from '@hsm-lib/definitions/enums/modules/security/roles';

import type { IUser } from '@hsm-lib/definitions/interfaces';

@Injectable()
export class UsersService {
  constructor(@InjectRepository(UserEntity) private readonly userRepository: Repository<UserEntity>) {}
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

  async findByUsername(username: string): Promise<IUser | undefined> {
    return this.users.find(user => user.username === username);
  }

  async createUser(user: Omit<IUser, 'id'>): Promise<IUser> {
    const newUser: IUser = {
      id: (this.users.length + 1).toString(),
      ...user,
    };
    this.users.push(newUser);
    return newUser;
  }
}
