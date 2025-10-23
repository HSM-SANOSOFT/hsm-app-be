import { Injectable, UnauthorizedException } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';

import { GenerateIntegrationTokenDto } from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import { IJwtPayload, ISignedUser, ITokens, IUnsignedUser, IUser, LoginResponse } from '@hsm-lib/definitions/interfaces';

import { UsersService } from '../../core/users/users.service';

@Injectable()
export class AuthService {
  constructor(private usersService: UsersService, private jwtService: JwtService) {}

  async validateUser(username: string, pass: string): Promise<IUnsignedUser> {
    const user = await this.usersService.findByUsername(username);
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

  async signup(newUser: Omit<IUser, 'id'>): Promise<ITokens> {
    const user = await this.usersService.createUser(newUser);
    const tokens: ITokens = await this.generateTokens(user);
    return tokens;
  }

  async login(user: IUnsignedUser): Promise<LoginResponse> {
    const response: LoginResponse = await this.generateTokens(user);
    return response;
  }

  async logout() {}

  async refresh() {}

  async generateIntegrationToken(payload: GenerateIntegrationTokenDto) {
    const expiresIn = payload.expiresIn ?? '100y';
    const roles = Role.System.Integration;
    const jwtPayload = { sub: payload.name, description: payload.description, functionality: payload.functionality, roles };
    return {
      integration_token: this.jwtService.sign(jwtPayload, { expiresIn }),
    };
  }
}
