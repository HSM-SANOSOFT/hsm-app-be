import { Test } from '@nestjs/testing';

import { ComsController } from './coms.controller';

import type { TestingModule } from '@nestjs/testing';

describe('comsController', () => {
  let controller: ComsController;

  beforeEach(async () => {
    const module: TestingModule = await Test.createTestingModule({
      controllers: [ComsController],
    }).compile();

    controller = module.get<ComsController>(ComsController);
  });

  it('should be defined', () => {
    expect(controller).toBeDefined();
  });
});
