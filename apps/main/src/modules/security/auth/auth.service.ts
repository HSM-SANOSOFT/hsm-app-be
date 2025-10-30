import { envs } from '@hsm-lib/config';
import {
  RefreshTokenUserEntity,
  RefreshTokenUserIntegrationEntity,
} from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import {
  CreateTokenIntegrationPayloadDto,
  LoginPayloadDto,
  LogoutPayloadDto,
  SignupPayloadDto,
} from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import type {
  IJwtPayloadUser,
  IJwtPayloadUserIntegration,
  ITokens,
  IUnsignedUser,
  IUnsignedUserIntegration,
} from '@hsm-lib/definitions/interfaces';
import { Injectable, UnauthorizedException } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';
import { InjectDataSource, InjectRepository } from '@nestjs/typeorm';
import * as bcrypt from 'bcrypt';
import { DataSource } from 'typeorm';
import { Repository } from 'typeorm/repository/Repository.js';
import { UsersService } from '../../core/users/users.service';

@Injectable()
export class AuthService {
  constructor(
    private usersService: UsersService,
    @InjectRepository(RefreshTokenUserEntity, Databases.HsmDbPostgres)
    private refreshTokenUserRepository: Repository<RefreshTokenUserEntity>,
    @InjectRepository(
      RefreshTokenUserIntegrationEntity,
      Databases.HsmDbPostgres,
    )
    private refreshTokenUserIntegrationRepository: Repository<RefreshTokenUserIntegrationEntity>,
    private jwtService: JwtService,
    @InjectDataSource(Databases.HsmDbPostgres)
    private readonly dataSource: DataSource,
  ) {}

  async hashData(data: string): Promise<string> {
    return await bcrypt.hash(data, 10);
  }

  async refreshToken(
    user: IUnsignedUser | IUnsignedUserIntegration,
    refreshToken: string,
  ): Promise<void> {
    const integration: boolean = user.roles.includes(Role.System.Integration);
    const userId: string = user.id;
    if (integration) {
      await this.refreshTokenUserRepository.update(userId, {
        refreshToken: refreshToken,
      });
    } else {
      await this.refreshTokenUserIntegrationRepository.update(userId, {
        refreshToken: refreshToken,
      });
    }
  }

  async validateUser(username: string, pass: string): Promise<IUnsignedUser> {
    const user = await this.usersService.findOneByUsername(username);
    const passwordValid = await bcrypt.compare(pass, user.password);
    if (!passwordValid) {
      throw new UnauthorizedException('Invalid password');
    }

    console.log(JSON.stringify(user, null, 2));
    return {
      id: user.id,
      username: user.username,
      email: user.email,
      firstName: user.firstName,
      firstLastName: user.firstLastName,
      roles: user.roles.map(role => role.role),
    } as IUnsignedUser;
  }

  async generateTokens(
    user: IUnsignedUser | IUnsignedUserIntegration,
  ): Promise<ITokens> {
    const integration: boolean = user.roles.includes(Role.System.Integration);
    const payload: IJwtPayloadUser | IJwtPayloadUserIntegration = {
      sub: user.id,
      ...user,
    };
    const [access_token, refresh_token] = await Promise.all([
      this.jwtService.signAsync(payload, {
        expiresIn: '15m',
        secret: envs.JWT_AT_SECRET,
      }),
      this.jwtService.signAsync(payload, {
        expiresIn: integration ? '30d' : '7d',
        secret: envs.JWT_RT_SECRET,
      }),
    ]);
    return { access_token, refresh_token };
  }

  async signup(newUser: SignupPayloadDto): Promise<ITokens> {
    const queryRunner = this.dataSource.createQueryRunner();
    await queryRunner.connect();
    await queryRunner.startTransaction();
    try {
      const hashedPassword = await this.hashData(newUser.password);
      const user = await this.usersService.createUser(
        {
          ...newUser,
          password: hashedPassword,
        },
        queryRunner,
      );
      const userToSign: IUnsignedUser = {
        id: user.id,
        username: user.username,
        email: user.email,
        firstName: user.firstName,
        firstLastName: user.firstLastName,
        roles: newUser.roles,
      };
      const tokens: ITokens = await this.generateTokens(userToSign);
      const refreshToken = await this.hashData(tokens.refresh_token);
      await queryRunner.manager.save(RefreshTokenUserEntity, {
        user: user,
        refreshToken: refreshToken,
        isActive: true,
      });
      await queryRunner.commitTransaction();
      return tokens;
    } catch (error) {
      await queryRunner.rollbackTransaction();
      throw error;
    } finally {
      await queryRunner.release();
    }
  }

  async login(user: IUnsignedUser): Promise<ITokens> {
    const tokens: ITokens = await this.generateTokens(user);
    const refreshToken = await this.hashData(tokens.refresh_token);
    await this.refreshToken(user, refreshToken);
    return tokens;
  }

  async logout(user: LogoutPayloadDto) {
    const { id } = user;
    const response = await this.refreshTokenUserRepository.update(id, {
      isActive: false,
    });
    if (!response.affected) {
      throw new UnauthorizedException('User not found');
    }
  }

  async createTokenIntegration(
    payload: CreateTokenIntegrationPayloadDto,
  ): Promise<ITokens> {
    const queryRunner = this.dataSource.createQueryRunner();
    await queryRunner.connect();
    await queryRunner.startTransaction();

    try {
      const user = await this.usersService.createUserIntegration(
        payload,
        queryRunner,
      );

      const userToSign: IUnsignedUserIntegration = {
        id: user.id,
        name: user.name,
        roles: [Role.System.Integration],
      };

      const tokens: ITokens = await this.generateTokens(userToSign);
      const refreshToken = await this.hashData(tokens.refresh_token);
      await queryRunner.manager.save(RefreshTokenUserIntegrationEntity, {
        user: user,
        refreshToken: refreshToken,
        isActive: true,
      });
      await queryRunner.commitTransaction();
      return tokens;
    } catch (error) {
      await queryRunner.rollbackTransaction();
      throw error;
    } finally {
      await queryRunner.release();
    }
  }

  async refresh() {
    return 'hello';
  }
}
