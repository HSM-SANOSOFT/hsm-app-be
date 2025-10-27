import {
  type GenerateIntegrationTokenDto,
  LoginPayloadDto,
  type LogoutPayloadDto,
  type SignupPayloadDto,
} from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import { type ITokens, LoginResponse } from '@hsm-lib/definitions/interfaces';
import {
  Body,
  Controller,
  Get,
  Param,
  Post,
  Req,
  UseGuards,
} from '@nestjs/common';
import { AuthGuard } from '@nestjs/passport';
import type { Request } from 'express';
import { Roles } from '../roles/roles.decorator';
import { Public } from './auth.decorator';
import type { AuthService } from './auth.service';

@Controller('auth')
export class AuthController {
  constructor(private authService: AuthService) {}

  @Public()
  @Post('signup')
  async signup(@Body() signupDto: SignupPayloadDto): Promise<ITokens> {
    const newUser = await this.authService.signup(signupDto);
    return newUser;
  }

  // @Public()
  // @UseGuards(AuthGuard('local'))
  // @Post('login')
  // async login(@Body() loginDto: LoginPayloadDto): Promise<LoginResponse> {
  //  const user = await this.authService.validateUser(loginDto.username, loginDto.password);
  //  return await this.authService.login(user);
  // }

  @Get('logout/:id')
  async logout(@Param('id') logoutDto: LogoutPayloadDto) {
    return await this.authService.logout(logoutDto);
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
