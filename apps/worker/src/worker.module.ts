import { DatabaseModule } from '@hsm-lib/database';
import { QueueModule } from '@hsm-lib/queue/queue.module';
import { Module } from '@nestjs/common';
import { CoreModule } from './modules/core/core.module';
import { WorkerController } from './worker.controller';
import { WorkerService } from './worker.service';

@Module({
  imports: [DatabaseModule, QueueModule, CoreModule],
  controllers: [WorkerController],
  providers: [WorkerService],
})
export class WorkerModule {}
