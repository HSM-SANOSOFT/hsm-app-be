import { envs } from '@hsm-lib/config';
import {
  RefreshTokenUserEntity,
  RefreshTokenUserIntegrationEntity,
} from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import {
  CreateTokenIntegrationPayloadDto,
  LogoutPayloadDto,
  SignupPayloadDto,
} from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import {
  IJwtPayloadUser,
  IJwtPayloadUserIntegration,
  IRefreshUser,
  ITokens,
  IUnsignedUser,
  IUnsignedUserIntegration,
} from '@hsm-lib/definitions/interfaces';
import { Injectable, Logger, UnauthorizedException } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';
import { InjectDataSource, InjectRepository } from '@nestjs/typeorm';
import * as bcrypt from 'bcrypt';
import { DataSource } from 'typeorm';
import { Repository } from 'typeorm/repository/Repository.js';
import { UsersService } from '../../core/users/users.service';

@Injectable()
export class AuthService {
  private readonly logger = new Logger(AuthService.name);
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
    const queryRunner = this.dataSource.createQueryRunner();
    await queryRunner.connect();
    await queryRunner.startTransaction();

    try {
      if (integration) {
        await queryRunner.manager.update(
          RefreshTokenUserEntity,
          { user: { id: userId }, isActive: true },
          {
            isActive: false,
          },
        );
        await queryRunner.manager.save(RefreshTokenUserIntegrationEntity, {
          user: { id: userId },
          refreshToken: refreshToken,
          isActive: true,
        });
      } else {
        await queryRunner.manager.update(
          RefreshTokenUserIntegrationEntity,
          { user: { id: userId }, isActive: true },
          {
            isActive: false,
          },
        );
        await queryRunner.manager.save(RefreshTokenUserEntity, {
          user: { id: userId },
          refreshToken: refreshToken,
          isActive: true,
        });
      }

      await queryRunner.commitTransaction();
    } catch (error) {
      await queryRunner.rollbackTransaction();
      throw error;
    } finally {
      await queryRunner.release();
    }
  }

  async test() {
    return await this.usersService.findOneByUsername('rsantamaria1');
  }
  async validateUser(username: string, pass: string): Promise<IUnsignedUser> {
    const user = await this.usersService.findOneByUsername(username);
    const passwordValid = await bcrypt.compare(pass, user.password);
    if (!passwordValid) {
      throw new UnauthorizedException('Invalid password');
    }
    const userRoles = user.roles.map(role => role.role);
    const unsignedUser: IUnsignedUser = {
      id: user.id,
      username: user.username,
      email: user.email,
      firstName: user.firstName,
      firstLastName: user.firstLastName,
      roles: userRoles,
    };
    this.logger.debug('Validate User', unsignedUser);
    return unsignedUser;
  }

  async validateRefreshToken(
    user: IRefreshUser,
  ): Promise<IUnsignedUser | IUnsignedUserIntegration> {
    const { refreshToken, iat: _iat, exp: _exp, ...userData } = user;
    const integration: boolean = user.roles.includes(Role.System.Integration);
    const userId: string = user.id;
    let refreshTokenInDb:
      | RefreshTokenUserEntity
      | RefreshTokenUserIntegrationEntity
      | null;
    if (integration) {
      this.logger.debug('Refresh Token User Repository Integration:');
      refreshTokenInDb =
        await this.refreshTokenUserIntegrationRepository.findOne({
          where: { user: { id: userId } },
        });
    } else {
      this.logger.debug('Refresh Token User Repository:');
      refreshTokenInDb = await this.refreshTokenUserRepository.findOne({
        where: { user: { id: userId } },
      });
    }
    if (!refreshTokenInDb) {
      throw new UnauthorizedException('Refresh token not found');
    }

    if (!refreshTokenInDb.isActive) {
      throw new UnauthorizedException('Refresh token is not active');
    }

    const refreshTokenValid = await bcrypt.compare(
      refreshToken,
      refreshTokenInDb.refreshToken,
    );

    if (!refreshTokenValid) {
      throw new UnauthorizedException('Refresh token is not valid');
    }
    return userData;
  }

  async generateTokens(
    user: IUnsignedUser | IUnsignedUserIntegration,
  ): Promise<ITokens> {
    const integration: boolean = user.roles.includes(Role.System.Integration);
    const payload: IJwtPayloadUser | IJwtPayloadUserIntegration = {
      sub: user.id,
      ...user,
    };
    this.logger.debug('generate token Payload', payload);
    const [access_token, refresh_token] = await Promise.all([
      this.jwtService.signAsync(payload, {
        expiresIn: '1m',
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

  async signupIntegration(
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

  async refresh(user: IRefreshUser): Promise<ITokens> {
    const userToSign = await this.validateRefreshToken(user);
    this.logger.debug('Service', userToSign);
    const tokens: ITokens = await this.generateTokens(userToSign);
    const newRefreshToken = await this.hashData(tokens.refresh_token);
    await this.refreshToken(user, newRefreshToken);
    return tokens;
  }
}
