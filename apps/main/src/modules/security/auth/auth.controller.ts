import {
  LoginPayloadDto,
  LogoutIntegrationTokenPayloadDto,
  SignupIntegrationTokenPayloadDto,
  SignupPayloadDto,
} from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import type {
  IRefreshUser,
  ISignedUser,
  ITokens,
} from '@hsm-lib/definitions/interfaces';
import { Body, Controller, Get, Post, Req, UseGuards } from '@nestjs/common';
import { AuthGuard } from '@nestjs/passport';
import type { Request } from 'express';
import { Roles } from '../roles/roles.decorator';
import { Public } from './auth.decorator';
import { AuthService } from './auth.service';

@Controller('auth')
export class AuthController {
  constructor(private authService: AuthService) {}

  @Public()
  @Post('signup')
  async signup(@Body() payload: SignupPayloadDto): Promise<ITokens> {
    return await this.authService.signup(payload);
  }

  @Public()
  @UseGuards(AuthGuard('local'))
  @Post('login')
  async login(@Req() req: Request, @Body() _payload: LoginPayloadDto) {
    return await this.authService.login(req.user as ISignedUser);
  }

  @Public()
  @Get('logout')
  async logout(@Req() req: Request) {
    const token = req.headers.authorization?.split(' ')[1];
    return await this.authService.logout(token);
  }

  @UseGuards(AuthGuard('jwt-rt'))
  @Get('refresh')
  async refresh(@Req() req: Request) {
    const user = req.user as IRefreshUser;
    return await this.authService.refresh(user);
  }

  @Post('signup/integration')
  @Roles(Role.System.Admin)
  async signupIntegration(@Body() payload: SignupIntegrationTokenPayloadDto) {
    return await this.authService.signupIntegration(payload);
  }

  @Post('logout/integration')
  @Roles(Role.System.Admin)
  async logoutIntegration(@Body() payload: LogoutIntegrationTokenPayloadDto) {
    const token = payload.token;
    return await this.authService.logoutIntegration(token);
  }

  @Get('profile')
  profile(@Req() req: Request) {
    return req.user;
  }
}
