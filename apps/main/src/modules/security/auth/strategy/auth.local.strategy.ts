import { IUnsignedUser } from '@hsm-lib/definitions/interfaces';
import { Injectable, Logger } from '@nestjs/common';
import { PassportStrategy } from '@nestjs/passport';
import { Strategy } from 'passport-local';
import { AuthService } from '../auth.service';

@Injectable()
export class AuthLocalStrategy extends PassportStrategy(Strategy, 'local') {
  private readonly logger = new Logger(AuthLocalStrategy.name);
  constructor(private authService: AuthService) {
    super();
  }

  async validate(username: string, password: string): Promise<IUnsignedUser> {
    const user = await this.authService.validateUser(username, password);
    this.logger.debug(`local strategy validate user:`, user);
    return user;
  }
}
