import { Test } from '@nestjs/testing';

import { UserService } from './user.service';

import type { TestingModule } from '@nestjs/testing';

describe('userService', () => {
  let service: UserService;

  beforeEach(async () => {
    const module: TestingModule = await Test.createTestingModule({
      providers: [UserService],
    }).compile();

    service = module.get<UserService>(UserService);
  });

  it('should be defined', () => {
    expect(service).toBeDefined();
  });
});
