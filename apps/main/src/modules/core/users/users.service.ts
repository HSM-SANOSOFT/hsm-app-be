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
import { Injectable, NotFoundException } from '@nestjs/common';
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

  async findOneByUsername(username: string): Promise<UserEntity> {
    const user = await this.UserRepository.findOne({ where: { username } });
    if (!user) {
      throw new NotFoundException(`User with username ${username} not found`);
    }
    const userRoles = await this.UserRoleRepository.find({
      where: { user: user },
    });

    user.roles = userRoles;
    return user;
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
    queryRunner: QueryRunner,
  ): Promise<UserIntegrationEntity> {
    return await queryRunner.manager.save(UserIntegrationEntity, user);
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
