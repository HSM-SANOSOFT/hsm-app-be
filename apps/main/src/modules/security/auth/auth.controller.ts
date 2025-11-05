import {
  CreateTokenIntegrationPayloadDto,
  LoginPayloadDto,
  LogoutPayloadDto,
  SignupPayloadDto,
} from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import type {
  IRefreshUser,
  ISignedUser,
  ISignedUserIntegration,
  ITokens,
} from '@hsm-lib/definitions/interfaces';
import {
  Body,
  Controller,
  Get,
  Logger,
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
  private readonly logger = new Logger(AuthController.name);
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
    const tokens = await this.authService.login(req.user as ISignedUser);
    return tokens;
  }

  @Public()
  @Get('logout')
  async logout(@Req() req: Request) {
    const user = req.user as ISignedUser | ISignedUserIntegration;
    const logoutDto: LogoutPayloadDto = { id: user.id };
    return await this.authService.logout(logoutDto);
  }

  @UseGuards(AuthGuard('jwt-rt'))
  @Get('refresh')
  async refresh(@Req() req: Request) {
    const user = req.user as IRefreshUser;
    this.logger.debug('refresh controller', user);
    return await this.authService.refresh(user);
  }

  @Post('signup/integration')
  @Roles(Role.System.Admin)
  async signupIntegration(@Body() payload: CreateTokenIntegrationPayloadDto) {
    return await this.authService.signupIntegration(payload);
  }

  @Get('profile')
  profile(@Req() req: Request) {
    return req.user;
  }

  @Public()
  @Get('test')
  async test() {
    return await this.authService.test();
  }
}
