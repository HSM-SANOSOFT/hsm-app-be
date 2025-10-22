import { Module } from '@nestjs/common';
import { JwtModule } from '@nestjs/jwt';
import { PassportModule } from '@nestjs/passport';

import { envs } from '@hsm-lib/config';

import { UsersModule } from '../../core/users/users.module';
import { AuthController } from './auth.controller';
import { AuthService } from './auth.service';
import { AuthJwtGuard } from './guard/auth.jwt.guard';
import { AuthJwtStrategy } from './strategy/auth.jwt.strategy';
import { AuthLocalStrategy } from './strategy/auth.local.strategy';

@Module({
  imports: [UsersModule, PassportModule, JwtModule.register({
    secret: envs.JWT_SECRET,
    global: true,
    signOptions: { expiresIn: '1d' },
  })],
  controllers: [AuthController],
  providers: [AuthService, AuthLocalStrategy, AuthJwtStrategy, {
    provide: 'AUTH_JWT_GUARD',
    useClass: AuthJwtGuard,
  }],
})
export class AuthModule {}
