import { Module } from '@nestjs/common';

import { DatabaseModule } from '@hsm-lib/database';
import { QueueModule } from '@hsm-lib/queue/queue.module';

import { WorkerController } from './worker.controller';
import { WorkerService } from './worker.service';
import { CoreModule } from './modules/core/core.module';

@Module({
  imports: [DatabaseModule, QueueModule, CoreModule],
  controllers: [WorkerController],
  providers: [WorkerService],
})
export class WorkerModule {}
