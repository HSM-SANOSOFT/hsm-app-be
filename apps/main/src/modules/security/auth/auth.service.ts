import type { UserEntity } from '@hsm-lib/database/entities';
import {
  type GenerateIntegrationTokenDto,
  LoginPayloadDto,
  type LogoutPayloadDto,
  type SignupPayloadDto,
} from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import type {
  IJwtPayload,
  ISignedUser,
  ITokens,
  IUnsignedUser,
  LoginResponse,
} from '@hsm-lib/definitions/interfaces';
import { Injectable, UnauthorizedException } from '@nestjs/common';
import type { JwtService } from '@nestjs/jwt';

import type { UsersService } from '../../core/users/users.service';

@Injectable()
export class AuthService {
  constructor(
    private usersService: UsersService,
    private jwtService: JwtService,
  ) {}

  async validateUser(username: string, pass: string): Promise<IUnsignedUser> {
    const user = await this.usersService.findOneByUsername(username);
    if (!user) {
      throw new UnauthorizedException('User not found');
    }

    if (user.password !== pass) {
      throw new UnauthorizedException('Invalid password');
    }

    const result: IUnsignedUser = { ...user };
    return result;
  }

  async generateTokens(user: IUnsignedUser): Promise<ITokens> {
    const userToSign: ISignedUser = { ...user };
    const jwtPayload: IJwtPayload = { sub: user.id, ...userToSign };
    const [access_token, refresh_token] = await Promise.all([
      this.jwtService.signAsync(jwtPayload, { expiresIn: '15m' }),
      this.jwtService.signAsync(jwtPayload, { expiresIn: '7d' }),
    ]);
    return { access_token, refresh_token };
  }

  async signup(newUser: SignupPayloadDto): Promise<ITokens> {
    const user = await this.usersService.createUser(newUser);
    const tokens: ITokens = await this.generateTokens(user);
    return tokens;
  }

  async login(user: UserEntity): Promise<LoginResponse> {
    const response: LoginResponse = await this.generateTokens(user);
    return response;
  }

  async logout(id: LogoutPayloadDto) {
    return id;
  }

  async refresh() {}

  async generateIntegrationToken(payload: GenerateIntegrationTokenDto) {
    const expiresIn = payload.expiresIn ?? '100y';
    const roles = Role.System.Integration;
    const jwtPayload = {
      sub: payload.name,
      description: payload.description,
      functionality: payload.functionality,
      roles,
    };
    return {
      integration_token: this.jwtService.sign(jwtPayload, { expiresIn }),
    };
  }
}
