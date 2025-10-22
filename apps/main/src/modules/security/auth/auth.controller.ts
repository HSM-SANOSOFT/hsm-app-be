import { Controller, Get, Post, Request, UseGuards } from '@nestjs/common';
import { AuthGuard } from '@nestjs/passport';

import { Public } from './auth.decorator';
import { AuthService } from './auth.service';

import type { IUser } from '@hsm-lib/definitions/interfaces';

@Controller('auth')
export class AuthController {
  constructor(private authService: AuthService) {}

  @Public()
  @UseGuards(AuthGuard('local'))
  @Post('login')
  login(@Request() req) {
    return this.authService.login(req.user);
  }

  @Get('profile')
  profile(@Request() req) {
    return (req.user);
  }
}
