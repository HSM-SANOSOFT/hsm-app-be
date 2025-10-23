import { Body, Controller, Get, Post, Req, UseGuards } from '@nestjs/common';
import { AuthGuard } from '@nestjs/passport';

import { GenerateIntegrationTokenDto } from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import { IUser, SigninResponse } from '@hsm-lib/definitions/interfaces';

import { Roles } from '../roles/roles.decorator';
import { Public } from './auth.decorator';
import { AuthService } from './auth.service';

import type { Request } from 'express';

@Controller('auth')
export class AuthController {
  constructor(private authService: AuthService) {}

  @Public()
  @UseGuards(AuthGuard('local'))
  @Post('login')
  login(@Req() req: Request): Promise<SigninResponse> {
    const user = req.user as Omit<IUser, 'password'>;
    return this.authService.signin(user);
  }
  
  @Get("logout")

  @Get('refresh')

  @Post('token/integration')
  @Roles(Role.System.Admin)
  async generateIntegrationToken(@Body() payload: GenerateIntegrationTokenDto) {
    return await this.authService.generateIntegrationToken(payload);
  }

  @Get('profile')
  profile(@Req() req: Request) {
    return req.user;
  }
}
