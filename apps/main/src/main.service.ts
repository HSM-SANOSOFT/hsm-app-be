import { Injectable, Logger, OnModuleInit } from '@nestjs/common';

@Injectable()
export class MainService implements OnModuleInit {
  private readonly logger = new Logger(MainService.name);

  constructor() {}

  getHello(): string {
    return 'Hello World!';
  }

  onModuleInit() {
    this.logger.log('Environment variables loaded:');
  }
}
