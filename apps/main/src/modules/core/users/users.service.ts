import { UserEntity, UserIntegrationEntity } from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import type {
  CreateUserIntegrationPayloadDto,
  CreateUserPayloadDto,
  DeleteUserPayloadDto,
  UpdateUserPayloadDto,
} from '@hsm-lib/definitions/dtos';
import { Injectable } from '@nestjs/common';
import { InjectRepository } from '@nestjs/typeorm';
import type { Repository } from 'typeorm';

@Injectable()
export class UsersService {
  constructor(
    @InjectRepository(UserEntity, Databases.HsmDbPostgres)
    private UserRepository: Repository<UserEntity>,
    @InjectRepository(UserIntegrationEntity, Databases.HsmDbPostgres)
    private UserIntegrationRepository: Repository<UserIntegrationEntity>,
  ) {}

  async findOneByUsername(username: string): Promise<UserEntity | null> {
    return await this.UserRepository.findOne({ where: { username } });
  }

  async createUser(user: CreateUserPayloadDto): Promise<UserEntity> {
    const newUser = this.UserRepository.create(user);
    return await this.UserRepository.save(newUser);
  }

  async createUserIntegration(
    user: CreateUserIntegrationPayloadDto,
  ): Promise<UserIntegrationEntity> {
    const newUserIntegration = this.UserIntegrationRepository.create(user);
    return await this.UserIntegrationRepository.save(newUserIntegration);
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
