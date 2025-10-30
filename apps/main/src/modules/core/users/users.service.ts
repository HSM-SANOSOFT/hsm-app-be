import {
  UserEntity,
  UserIntegrationEntity,
  UserRoleEntity,
} from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import {
  CreateUserIntegrationPayloadDto,
  CreateUserPayloadDto,
  DeleteUserPayloadDto,
  UpdateUserPayloadDto,
} from '@hsm-lib/definitions/dtos';
import { Injectable } from '@nestjs/common';
import { InjectDataSource, InjectRepository } from '@nestjs/typeorm';
import type { QueryRunner } from 'typeorm';
import { DataSource, Repository } from 'typeorm';
import { RolesService } from '../../security/roles/roles.service';

@Injectable()
export class UsersService {
  constructor(
    @InjectRepository(UserEntity, Databases.HsmDbPostgres)
    private UserRepository: Repository<UserEntity>,
    @InjectRepository(UserIntegrationEntity, Databases.HsmDbPostgres)
    private UserIntegrationRepository: Repository<UserIntegrationEntity>,
    @InjectRepository(UserRoleEntity, Databases.HsmDbPostgres)
    private UserRoleRepository: Repository<UserRoleEntity>,
    private readonly rolesService: RolesService,
    @InjectDataSource(Databases.HsmDbPostgres)
    private readonly dataSource: DataSource,
  ) {}

  async findOneByUsername(username: string): Promise<UserEntity | null> {
    return await this.UserRepository.findOne({ where: { username } });
  }

  async createUser(
    user: CreateUserPayloadDto,
    queryRunner: QueryRunner,
  ): Promise<UserEntity> {
    const { roles, ...userData } = user;
    const roleDomains = this.rolesService.findRoleDomains(roles);
    const newUser = await queryRunner.manager.save(UserEntity, userData);
    await Promise.all(
      roleDomains.map(({ role, domain }) =>
        queryRunner.manager.save(UserRoleEntity, {
          user: newUser,
          role,
          domain,
        }),
      ),
    );
    return newUser;
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
