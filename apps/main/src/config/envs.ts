import 'dotenv/config';

import { Logger } from '@nestjs/common';
const logger = new Logger('EnvConfig');
//import * as joi from 'joi';

/*interface EnvVars {}

const envSchema = joi.object({}).unknown().required();

const validation = envSchema.validate(process.env);

if (validation.error) {
  throw new Error(`Config validation error: ${validation.error.message}`);
}

const envVars: EnvVars = validation.value as EnvVars;

export const envs = {};
*/

logger.log('Environment variables loaded:', process.env);
