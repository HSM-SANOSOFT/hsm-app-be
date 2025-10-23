import { Injectable, UnauthorizedException } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';

import { GenerateIntegrationTokenDto } from '@hsm-lib/definitions/dtos/modules/security/auth';
import { Role } from '@hsm-lib/definitions/enums/modules/security/roles';
import { IUser } from '@hsm-lib/definitions/interfaces';
import { JwtPayload } from '@hsm-lib/definitions/types/modules/security/auth';

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

  async login(user: Omit<IUser, 'password'>) {
    const { id, ...rest } = user;

    const jwtPayload: JwtPayload = { sub: id, ...rest };
    return {
      access_token: this.jwtService.sign(jwtPayload),
    };
  }

  async generateIntegrationToken(payload: GenerateIntegrationTokenDto) {
    const { expiresIn, ...rest } = payload;
    const roles = Role.System.Integration;
    const jwtPayload = { ...rest, roles };

    return {
      integration_token: this.jwtService.sign(jwtPayload, { expiresIn: expiresIn ?? '100y' }),
    };
  }
}
