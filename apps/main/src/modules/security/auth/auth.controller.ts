import {
  CreateTokenIntegrationPayloadDto,
  LoginPayloadDto,
  LogoutPayloadDto,
  SignupPayloadDto,
} from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import type { ITokens, IUnsignedUser } from '@hsm-lib/definitions/interfaces';
import {
  Body,
  Controller,
  Delete,
  Get,
  Param,
  Patch,
  Post,
  Req,
  UseGuards,
} from '@nestjs/common';
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
  async login(@Req() req: Request): Promise<ITokens> {
    const user = req.user as IUnsignedUser;
    return await this.authService.login(user);
  }

  @Public()
  @Get('logout')
  async logout(@Req() req: Request) {
    const user = req.user;
    const logoutDto: LogoutPayloadDto = { id: user.sub };
    return await this.authService.logout(logoutDto);
  }

  @UseGuards(AuthGuard('jwt-refresh'))
  @Get('refresh')
  async refresh() {
    return await this.authService.refresh();
  }

  @Post('token/integration')
  @Roles(Role.System.Admin)
  async createTokenIntegration(
    @Body() payload: CreateTokenIntegrationPayloadDto,
  ) {
    return await this.authService.createTokenIntegration(payload);
  }

  @Patch('token/integration/:id')
  @Roles(Role.System.Admin)
  async deactivateIntegrationToken(@Param('id') id: string) {
    // return await this.authService.deactivateIntegrationToken(id);
  }

  @Delete('token/integration/:id')
  @Roles(Role.System.Admin)
  async deleteIntegrationToken(@Param('id') id: string) {
    //return await this.authService.deleteIntegrationToken(id);
  }

  @Get('profile')
  profile(@Req() req: Request) {
    return req.user;
  }
}
