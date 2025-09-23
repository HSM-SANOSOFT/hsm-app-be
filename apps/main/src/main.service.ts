import type { Envs } from '@hsm-lib/config';
import { envs } from '@hsm-lib/config';
import { Inject, Injectable } from '@nestjs/common';

@Injectable()
export class MainService {
  constructor(@Inject(envs) private readonly env: Envs) {}
  getHello(): string {
    return `Hello World!` + this.env.HSM_DB_POSTGRES_HOST;
  }
}
