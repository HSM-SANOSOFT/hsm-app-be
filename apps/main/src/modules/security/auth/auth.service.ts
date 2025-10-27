import {
  RefreshTokenUserEntity,
  RefreshTokenUserIntegrationEntity,
} from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import {
  CreateUserIntegrationDto,
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
import { InjectRepository } from '@nestjs/typeorm';
import * as bcrypt from 'bcrypt';
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

    const { password, ...result } = user;

    return result;
  }

  async generateTokens(
    user: IUnsignedUser | IUnsignedUserIntegration,
    integration: boolean = false,
  ): Promise<ITokens> {
    let payload: IJwtPayloadUser | IJwtPayloadUserIntegration;
    if (integration) {
      payload = {
        sub: user.id,
        ...user,
        roles: [Role.System.Integration],
      } as IJwtPayloadUserIntegration;
    } else {
      payload = { sub: user.id, ...user } as IJwtPayloadUser;
    }
    const [access_token, refresh_token] = await Promise.all([
      this.jwtService.signAsync(payload, { expiresIn: '15m' }),
      this.jwtService.signAsync(payload, {
        expiresIn: integration ? '7d' : '30d',
      }),
    ]);
    return { access_token, refresh_token };
  }

  async signup(newUser: SignupPayloadDto): Promise<ITokens> {
    const hashedPassword = await this.hashData(newUser.password);
    const user = await this.usersService.createUser({
      ...newUser,
      password: hashedPassword,
    });
    const tokens: ITokens = await this.generateTokens(user);
    const refreshToken = this.hashData(tokens.refresh_token);
    await this.refreshTokenUserRepository.save({
      user: user,
      refreshToken: await refreshToken,
      isActive: true,
    });
    return tokens;
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
    payload: CreateUserIntegrationDto,
  ): Promise<ITokens> {
    const user = await this.usersService.createUserIntegration(payload);

    const tokens: ITokens = await this.generateTokens(
      payload as IUnsignedUserIntegration,
      true,
    );
    const refreshToken = this.hashData(tokens.refresh_token);
    await this.refreshTokenUserIntegrationRepository.save({
      user: user,
      refreshToken: await refreshToken,
      isActive: true,
    });
    return tokens;
  }

  async logout(user: LogoutPayloadDto) {
    return user;
  }

  async refresh() {}
}
