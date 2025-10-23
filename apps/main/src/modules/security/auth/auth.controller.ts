import { Body, Controller, Get, Post, Req, UseGuards } from '@nestjs/common';
import { AuthGuard } from '@nestjs/passport';

import { GenerateIntegrationTokenDto } from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import { ITokens, IUnsignedUser, IUser, LoginResponse } from '@hsm-lib/definitions/interfaces';

import { Roles } from '../roles/roles.decorator';
import { Public } from './auth.decorator';
import { AuthService } from './auth.service';

import type { Request } from 'express';

@Controller('auth')
export class AuthController {
  constructor(private authService: AuthService) {}

  @Public()
  @Post('signup')
  async signup(@Body() user: Omit<IUser, 'id'>): Promise<ITokens> {
    const newUser = await this.authService.signup(user);
    return newUser;
  }

  @Public()
  @UseGuards(AuthGuard('local'))
  @Post('login')
  async login(@Req() req: Request): Promise<LoginResponse> {
    const user = req.user as IUnsignedUser;
    return await this.authService.login(user);
  }

  @Get('logout')
  async logout() {
    return await this.authService.logout();
  }

  @Get('refresh')
  async refresh() {
    return await this.authService.refresh();
  }

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
