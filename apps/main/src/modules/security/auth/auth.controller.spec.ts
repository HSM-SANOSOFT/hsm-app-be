import type { SignupPayloadDto } from '@hsm-lib/definitions/dtos';

import { Role } from '@hsm-lib/definitions/enums';
import type { TestingModule } from '@nestjs/testing';
import { Test } from '@nestjs/testing';
import { AuthController } from './auth.controller';
import { AuthService } from './auth.service';

describe('authController', () => {
  let controller: AuthController;

  class MockAuthService {
    signup = jest.fn();
    login = jest.fn();
    logout = jest.fn();
    refresh = jest.fn();
    generateIntegrationToken = jest.fn();
  }

  beforeEach(async () => {
    const module: TestingModule = await Test.createTestingModule({
      controllers: [AuthController],
      providers: [
        {
          provide: AuthService,
          useValue: MockAuthService,
        },
      ],
    }).compile();

    controller = module.get<AuthController>(AuthController);
  });

  it('should be defined', () => {
    expect(controller).toBeDefined();
  });

  it('should call signup method', async () => {
    const signupDto: SignupPayloadDto = {
      username: 'test',
      password: 'test',
      email: 'test@example.com',
      firstName: 'Test',
      firstLastName: 'User',
      gender: undefined,
      phoneNumber: undefined,
      secondLastName: undefined,
      secondName: undefined,
      role: [Role.System.Admin],
    };
    expect(await controller.signup(signupDto)).toBeDefined();
  });
});
