import { Test } from '@nestjs/testing';

import { UserController } from './user.controller';

import type { TestingModule } from '@nestjs/testing';

describe('userController', () => {
  let controller: UserController;

  beforeEach(async () => {
    const module: TestingModule = await Test.createTestingModule({
      controllers: [UserController],
    }).compile();

    controller = module.get<UserController>(UserController);
  });

  it('should be defined', () => {
    expect(controller).toBeDefined();
  });
});
