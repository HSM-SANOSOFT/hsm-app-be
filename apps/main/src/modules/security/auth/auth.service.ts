import { Injectable, UnauthorizedException } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';

import { GenerateIntegrationTokenDto } from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums/modules/security/roles';
import { IUser, SigninResponse } from '@hsm-lib/definitions/interfaces';
import { JwtPayload } from '@hsm-lib/definitions/types';

import { UsersService } from '../../core/users/users.service';

@Injectable()
export class AuthService {
  constructor(private usersService: UsersService, private jwtService: JwtService) {}

  async validateUser(username: string, pass: string): Promise<Omit<IUser, 'password'>> {
    const user = await this.usersService.findOne(username);
    if (!user) {
      throw new UnauthorizedException('User not found');
    }

    if (user.password !== pass) {
      throw new UnauthorizedException('Invalid password');
    }

    const { password, ...result } = user;
    return result;
  }

  async signin(user: Omit<IUser, 'password'>) {
    const { id, ...rest } = user;

    const jwtPayload: JwtPayload = { sub: id, ...rest };
    const response: SigninResponse = {
      access_token: this.jwtService.sign(jwtPayload, { expiresIn: '15m' }),
      refresh_token: this.jwtService.sign(jwtPayload, { expiresIn: '7d' }),
    };
    return response;
  }

  async generateIntegrationToken(payload: GenerateIntegrationTokenDto) {
    const expiresIn = payload.expiresIn ?? '100y';
    const roles = Role.System.Integration;
    const jwtPayload = { sub: payload.name, description: payload.description, functionality: payload.functionality, roles };
    return {
      integration_token: this.jwtService.sign(jwtPayload, { expiresIn }),
    };
  }
}
