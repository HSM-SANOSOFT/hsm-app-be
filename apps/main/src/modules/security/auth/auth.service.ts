import { envs } from '@hsm-lib/config';
import {
  RefreshTokenUserEntity,
  RefreshTokenUserIntegrationEntity,
} from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import {
  GenerateIntegrationTokenPayloadDto,
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

  async updateRefreshTokenHash(
    userId: string,
    refreshToken: string,
    integration: boolean = false,
  ): Promise<void> {
    const hashedRefreshToken = await this.hashData(refreshToken);
    if (integration) {
      await this.refreshTokenUserIntegrationRepository.update(userId, {
        refreshToken: hashedRefreshToken,
      });
    } else {
      await this.refreshTokenUserRepository.update(userId, {
        refreshToken: hashedRefreshToken,
      });
    }
  }

  async validateUser(username: string, pass: string): Promise<IUnsignedUser> {
    const user = await this.usersService.findOneByUsername(username);
    if (!user) {
      throw new UnauthorizedException('User not found');
    }
    const passwordValid = await bcrypt.compare(pass, user.password);
    if (!passwordValid) {
      throw new UnauthorizedException('Invalid password');
    }
    return {
      id: user.id,
      username: user.username,
      email: user.email,
      firstName: user.firstName,
      firstLastName: user.firstLastName,
      roles: user.roles.map(role => role.role),
    };
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

  async login(payload: LoginPayloadDto): Promise<ITokens> {
    const user: IUnsignedUser = await this.validateUser(
      payload.username,
      payload.password,
    );
    const tokens: ITokens = await this.generateTokens(user);
    await this.updateRefreshTokenHash(user.id, tokens.refresh_token);
    return tokens;
  }

  async generateIntegrationToken(
    payload: GenerateIntegrationTokenPayloadDto,
  ): Promise<ITokens> {
    const user = await this.usersService.createUserIntegration(payload);

    const userToSign: IUnsignedUserIntegration = {
      id: user.id,
      name: user.name,
      roles: [Role.System.Integration],
    };

    const tokens: ITokens = await this.generateTokens(userToSign);
    const refreshToken = await this.hashData(tokens.refresh_token);
    await this.refreshTokenUserIntegrationRepository.save({
      user: user,
      refreshToken: refreshToken,
      isActive: true,
    });
    return tokens;
  }

  async logout(user: LogoutPayloadDto) {
    return user;
  }

  async refresh() {}
}
