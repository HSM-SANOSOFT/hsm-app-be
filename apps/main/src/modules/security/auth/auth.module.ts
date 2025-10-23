import { Module } from '@nestjs/common';
import { JwtModule } from '@nestjs/jwt';
import { PassportModule } from '@nestjs/passport';

// import { envs } from '@hsm-lib/config';
import { UsersModule } from '../../core/users/users.module';
import { AuthController } from './auth.controller';
import { AuthService } from './auth.service';
import { AuthJwtGuard } from './guard';
import { AuthJwtATStrategy, AuthJwtRTStrategy, AuthLocalStrategy } from './strategy';

@Module({
  imports: [UsersModule, PassportModule, JwtModule.register({
    // secret: envs.JWT_AT_SECRET,
    global: true,
    // signOptions: { expiresIn: '15m' },
  })],
  controllers: [AuthController],
  providers: [AuthService, AuthLocalStrategy, AuthJwtATStrategy, AuthJwtRTStrategy, AuthJwtGuard],
  exports: [AuthJwtGuard],
})
export class AuthModule {}
