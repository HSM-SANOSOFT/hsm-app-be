import 'dotenv/config';

import * as process from 'node:process';

import * as joi from 'joi';

interface EnvVars {
  ENVIRONMENT: string;

  JWT_SECRET: string;

  HSM_DB_POSTGRES_HOST: string;
  HSM_DB_POSTGRES_PORT: number;
  HSM_DB_POSTGRES_USER: string;
  HSM_DB_POSTGRES_PASSWORD: string;
  HSM_DB_POSTGRES_DB: string;

  HSM_DB_ORACLE_HOST: string;
  HSM_DB_ORACLE_PORT: number;
  HSM_DB_ORACLE_USER: string;
  HSM_DB_ORACLE_PASSWORD: string;
  HSM_DB_ORACLE_DB: string;

  HSM_DB_REDIS_HOST: string;
  HSM_DB_REDIS_PORT: number;
  HSM_DB_REDIS_USER: string;
  HSM_DB_REDIS_PASSWORD: string;
}

const EnvSchema = joi
  .object({
    ENVIRONMENT: joi.string().required(),

    JWT_SECRET: joi.string().required(),

    HSM_DB_POSTGRES_HOST: joi.string().required(),
    HSM_DB_POSTGRES_PORT: joi.number().default(5432),
    HSM_DB_POSTGRES_USER: joi.string().required(),
    HSM_DB_POSTGRES_PASSWORD: joi.string().required(),
    HSM_DB_POSTGRES_DB: joi.string().required(),

    HSM_DB_ORACLE_HOST: joi.string().required(),
    HSM_DB_ORACLE_PORT: joi.number().default(1521),
    HSM_DB_ORACLE_USER: joi.string().required(),
    HSM_DB_ORACLE_PASSWORD: joi.string().required(),
    HSM_DB_ORACLE_DB: joi.string().required(),

    HSM_DB_REDIS_HOST: joi.string().required(),
    HSM_DB_REDIS_PORT: joi.number().default(6379),
    HSM_DB_REDIS_USER: joi.string().required(),
    HSM_DB_REDIS_PASSWORD: joi.string().required(),
  })
  .unknown()
  .required();

const validation = EnvSchema.validate(process.env);

if (validation.error) {
  throw new Error(`Config validation error: ${validation.error.message}`);
}

const envVars: EnvVars = validation.value as EnvVars;

export const envs = Object.freeze({
  ENVIRONMENT: envVars.ENVIRONMENT,

  JWT_SECRET: envVars.JWT_SECRET,

  HSM_DB_POSTGRES_HOST: envVars.HSM_DB_POSTGRES_HOST,
  HSM_DB_POSTGRES_PORT: envVars.HSM_DB_POSTGRES_PORT,
  HSM_DB_POSTGRES_USER: envVars.HSM_DB_POSTGRES_USER,
  HSM_DB_POSTGRES_PASSWORD: envVars.HSM_DB_POSTGRES_PASSWORD,
  HSM_DB_POSTGRES_DB: envVars.HSM_DB_POSTGRES_DB,

  HSM_DB_ORACLE_HOST: envVars.HSM_DB_ORACLE_HOST,
  HSM_DB_ORACLE_PORT: envVars.HSM_DB_ORACLE_PORT,
  HSM_DB_ORACLE_USER: envVars.HSM_DB_ORACLE_USER,
  HSM_DB_ORACLE_PASSWORD: envVars.HSM_DB_ORACLE_PASSWORD,
  HSM_DB_ORACLE_DB: envVars.HSM_DB_ORACLE_DB,

  HSM_DB_REDIS_HOST: envVars.HSM_DB_REDIS_HOST,
  HSM_DB_REDIS_PORT: envVars.HSM_DB_REDIS_PORT,
  HSM_DB_REDIS_USER: envVars.HSM_DB_REDIS_USER,
  HSM_DB_REDIS_PASSWORD: envVars.HSM_DB_REDIS_PASSWORD,
} as const);
export type Envs = Readonly<typeof envs>;
