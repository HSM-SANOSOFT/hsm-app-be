import {
  CreateUserIntegrationDto,
  LoginPayloadDto,
  LogoutPayloadDto,
  SignupPayloadDto,
} from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import type { ITokens } from '@hsm-lib/definitions/interfaces';
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
  async signup(@Body() payload: SignupPayloadDto) /*: Promise<ITokens>*/ {
    //const newUser = await this.authService.signup(payload);
    //return newUser;
    return payload;
  }

  @Public()
  @UseGuards(AuthGuard('local'))
  @Post('login')
  async login(@Body() payload: LoginPayloadDto): Promise<ITokens> {
    return await this.authService.login(payload);
  }

  @Get('logout/:id')
  async logout(@Param('id') id: string) {
    const logoutDto: LogoutPayloadDto = { id };
    return await this.authService.logout(logoutDto);
  }

  @Get('refresh')
  async refresh() {
    return await this.authService.refresh();
  }

  @Post('token/integration')
  @Roles(Role.System.Admin)
  async generateIntegrationToken(@Body() payload: CreateUserIntegrationDto) {
    return await this.authService.generateIntegrationToken(payload);
  }

  @Patch('token/integration/:id')
  @Roles(Role.System.Admin)
  async deactivateIntegrationToken(@Param('id') id: string) {
    //return await this.authService.deactivateIntegrationToken(id);
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
