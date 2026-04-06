import { AuthService } from '@hsm-app/server/modules/security/auth/auth.service';
import { IUnsignedUser } from '@hsm-lib/common/definitions/interfaces';
import { Injectable } from '@nestjs/common';
import { PassportStrategy } from '@nestjs/passport';
import { Strategy } from 'passport-local';

@Injectable()
export class AuthLocalStrategy extends PassportStrategy(Strategy, 'local') {
  constructor(private authService: AuthService) {
    super();
  }

  async validate(username: string, password: string): Promise<IUnsignedUser> {
    return await this.authService.validateUser(username, password);
  }
}
