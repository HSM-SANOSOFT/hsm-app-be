import { Test } from '@nestjs/testing';

import { DocsService } from './docs.service';

import type { TestingModule } from '@nestjs/testing';

describe('docsService', () => {
  let service: DocsService;

  beforeEach(async () => {
    const module: TestingModule = await Test.createTestingModule({
      providers: [DocsService],
    }).compile();

    service = module.get<DocsService>(DocsService);
  });

  it('should be defined', () => {
    expect(service).toBeDefined();
  });
});
