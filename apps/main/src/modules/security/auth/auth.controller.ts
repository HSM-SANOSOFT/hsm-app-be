import { Controller, Post } from '@nestjs/common';

import { Roles } from '../roles/roles.decorator';
import { Role } from '../roles/roles.enum';

@Controller('auth')
export class AuthController {
  @Roles(Role.System.Admin)
  @Post('login')
  login() {
    return 'Login successful';
  }
}
