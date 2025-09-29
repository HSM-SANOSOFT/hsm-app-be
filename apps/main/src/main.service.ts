import { Injectable } from '@nestjs/common';

import { envs } from '@hsm-lib/config';

@Injectable()
export class MainService {
  getHello(): string {
    return `Hello World!${envs.HSM_DB_POSTGRES_HOST}`;
  }
}
