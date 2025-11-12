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
import { ApiBearerAuth } from '@nestjs/swagger';
import type { Request } from 'express';
import { ApiDocumentation } from '../../../common/decorator';
import { Roles } from '../roles/roles.decorator';
import { Public } from './auth.decorator';
import { AuthService } from './auth.service';

@Controller('auth')
export class AuthController {
  constructor(private authService: AuthService) {}

  @Post('signup')
  @Public()
  @ApiDocumentation()
  async signup(@Body() payload: SignupPayloadDto): Promise<ITokens> {
    return await this.authService.signup(payload);
  }

  @Public()
  @UseGuards(AuthGuard('local'))
  @ApiDocumentation()
  @Post('login')
  async login(@Req() req: Request, @Body() _payload: LoginPayloadDto) {
    return await this.authService.login(req.user as ISignedUser);
  }

  @Get('logout')
  @Public()
  @ApiDocumentation()
  async logout(@Req() req: Request) {
    const token = req.headers.authorization?.split(' ')[1];
    return await this.authService.logout(token);
  }

  @Get('refresh')
  @UseGuards(AuthGuard('jwt-rt'))
  @ApiDocumentation()
  async refresh(@Req() req: Request) {
    const user = req.user as IRefreshUser;
    return await this.authService.refresh(user);
  }

  @Post('signup/integration')
  @Roles(Role.System.Admin)
  @ApiDocumentation()
  async signupIntegration(@Body() payload: SignupIntegrationTokenPayloadDto) {
    return await this.authService.signupIntegration(payload);
  }

  @Post('logout/integration')
  @Roles(Role.System.Admin)
  @ApiBearerAuth()
  @ApiDocumentation()
  async logoutIntegration(@Body() payload: LogoutIntegrationTokenPayloadDto) {
    const token = payload.token;
    return await this.authService.logoutIntegration(token);
  }

  @Get('profile')
  @ApiDocumentation()
  profile(@Req() req: Request) {
    return req.user;
  }
}
