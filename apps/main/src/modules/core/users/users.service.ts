import { Injectable } from '@nestjs/common';
import { InjectRepository } from '@nestjs/typeorm';
import { Repository } from 'typeorm';

import { UserEntity } from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import {
  CreateUserPayloadDto,
  DeleteUserPayloadDto,
  UpdateUserPayloadDto,
} from '@hsm-lib/definitions/dtos';

@Injectable()
export class UsersService {
  constructor(
    @InjectRepository(UserEntity, Databases.HsmDbPostgres)
    private UserRepository: Repository<UserEntity>,
  ) {}

  async findOneByUsername(username: string): Promise<UserEntity | null> {
    return await this.UserRepository.findOne({ where: { username } });
  }

  async createUser(user: CreateUserPayloadDto): Promise<UserEntity> {
    const newUser = this.UserRepository.create(user);
    return await this.UserRepository.save(newUser);
  }

  async updateUser(user: UpdateUserPayloadDto): Promise<UserEntity | null> {
    const { id } = user;
    const response = await this.UserRepository.update(id, user);
    if (!response.affected) {
      return null;
    }
    const updatedUser = await this.UserRepository.findOne({ where: { id } });
    if (!updatedUser) {
      return null;
    }
    return updatedUser;
  }

  async deleteUser(user: DeleteUserPayloadDto): Promise<void> {
    await this.UserRepository.delete(user.id);
  }
}
