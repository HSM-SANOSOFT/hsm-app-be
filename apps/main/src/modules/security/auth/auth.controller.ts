import { ApiDocumentation, Public } from '@hsm-lib/common/decorator';
import {
  LoginPayloadDto,
  LogoutIntegrationTokenPayloadDto,
  SignupIntegrationTokenPayloadDto,
  SignupPayloadDto,
} from '@hsm-lib/definitions/dtos';
import {
  SignedIntegrationProfileDto,
  SignedUserProfileDto,
} from '@hsm-lib/definitions/dtos/modules/security/auth/profile-response.dto';
import { TokensDto } from '@hsm-lib/definitions/dtos/modules/security/auth/tokens.dto';
import { Role } from '@hsm-lib/definitions/enums';
import type {
  IRefreshUser,
  ISignedUser,
} from '@hsm-lib/definitions/interfaces';
import { Body, Controller, Get, Post, Req, UseGuards } from '@nestjs/common';
import type { Request } from 'express';
import { Roles } from '../roles/roles.decorator';
import { AuthService } from './auth.service';
import { AuthLocalGuard } from './guard';
import { AuthJwtRtGuard } from './guard/auth.jwt.rt.guard';

@Controller('auth')
export class AuthController {
  constructor(private authService: AuthService) {}

  @ApiDocumentation(TokensDto)
  @Public()
  @Post('signup')
  async signup(@Body() payload: SignupPayloadDto): Promise<TokensDto> {
    return await this.authService.signup(payload);
  }

  @ApiDocumentation(TokensDto)
  @UseGuards(AuthLocalGuard)
  @Public()
  @Post('login')
  async login(
    @Req() req: Request,
    @Body() _payload: LoginPayloadDto,
  ): Promise<TokensDto> {
    return await this.authService.login(req.user as ISignedUser);
  }

  @ApiDocumentation()
  @Public()
  @Get('logout')
  async logout(@Req() req: Request): Promise<void> {
    const token = req.headers.authorization?.split(' ')[1];
    return await this.authService.logout(token);
  }

  @ApiDocumentation(TokensDto)
  @UseGuards(AuthJwtRtGuard)
  @Get('refresh')
  async refresh(@Req() req: Request): Promise<TokensDto> {
    const user = req.user as IRefreshUser;
    return await this.authService.refresh(user);
  }

  @ApiDocumentation(TokensDto)
  @Roles(Role.System.Admin)
  @Post('signup/integration')
  async signupIntegration(
    @Body() payload: SignupIntegrationTokenPayloadDto,
  ): Promise<TokensDto> {
    return await this.authService.signupIntegration(payload);
  }

  @ApiDocumentation()
  @Roles(Role.System.Admin)
  @Post('logout/integration')
  async logoutIntegration(@Body() payload: LogoutIntegrationTokenPayloadDto) {
    const token = payload.token;
    return await this.authService.logoutIntegration(token);
  }

  @ApiDocumentation([SignedUserProfileDto, SignedIntegrationProfileDto])
  @Get('profile')
  profile(@Req() req: Request) {
    return req.user;
  }
}
