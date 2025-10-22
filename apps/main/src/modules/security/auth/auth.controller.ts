import { Controller, Get, Post, Req, UseGuards } from '@nestjs/common';
import { AuthGuard } from '@nestjs/passport';

import { IUser } from '@hsm-lib/definitions/interfaces/modules/core/users';

import { Public } from './auth.decorator';
import { AuthService } from './auth.service';

import type { Request } from 'express';

@Controller('auth')
export class AuthController {
  constructor(private authService: AuthService) {}

  @Public()
  @UseGuards(AuthGuard('local'))
  @Post('login')
  login(@Req() req: Request) {
    const user = req.user as Omit<IUser, 'password'>;
    return this.authService.login(user);
  }

  @Get('profile')
  profile(@Req() req: Request) {
    return req.user;
  }
}
