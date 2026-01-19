import 'dotenv/config';

import * as process from 'node:process';

import * as joi from 'joi';

interface EnvVars {
  ENVIRONMENT: string;

  SWAGGER_FAVICON: string;
  SWAGGER_SITE_TITLE: string;

  SMTP_ADDRESS: string;
  SMTP_USERNAME: string;
  SMTP_PASSWORD: string;
  SMTP_PORT: number;
  SMTP_FROM_EMAIL: string;
  SMTP_FROM_NAME: string;
  SMTP_SECURE: boolean;

  JWT_AT_SECRET: string;
  JWT_RT_SECRET: string;

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

  HSM_STORAGE_S3_ACCESS_KEY: string;
  HSM_STORAGE_S3_FORCE_PATH_STYLE: boolean;
  HSM_STORAGE_S3_HOST: string;
  HSM_STORAGE_S3_REGION: string;
  HSM_STORAGE_S3_SECRET_KEY: string;
}

const EnvSchema = joi
  .object({
    ENVIRONMENT: joi.string().required(),

    SWAGGER_FAVICON: joi.string().required(),
    SWAGGER_SITE_TITLE: joi.string().required(),

    SMTP_ADDRESS: joi.string().required(),
    SMTP_USERNAME: joi.string().required(),
    SMTP_PASSWORD: joi.string().required(),
    SMTP_PORT: joi.number().default(587),
    SMTP_FROM_EMAIL: joi.string().required(),
    SMTP_FROM_NAME: joi.string().required(),
    SMTP_SECURE: joi.boolean().default(true),

    JWT_AT_SECRET: joi.string().required(),
    JWT_RT_SECRET: joi.string().required(),

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

    HSM_STORAGE_S3_ACCESS_KEY: joi.string().required(),
    HSM_STORAGE_S3_FORCE_PATH_STYLE: joi.boolean().default(false),
    HSM_STORAGE_S3_HOST: joi.when('HSM_STORAGE_S3_FORCE_PATH_STYLE', {
      is: true,
      then: joi.string().trim().min(1).required(),
      otherwise: joi.string().forbidden(),
    }),
    HSM_STORAGE_S3_REGION: joi.string().default('us-east-1'),
    HSM_STORAGE_S3_SECRET_KEY: joi.string().required(),
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

  SWAGGER_FAVICON: envVars.SWAGGER_FAVICON,
  SWAGGER_SITE_TITLE: envVars.SWAGGER_SITE_TITLE,

  SMTP_ADDRESS: envVars.SMTP_ADDRESS,
  SMTP_USERNAME: envVars.SMTP_USERNAME,
  SMTP_PASSWORD: envVars.SMTP_PASSWORD,
  SMTP_PORT: envVars.SMTP_PORT,
  SMTP_FROM_EMAIL: envVars.SMTP_FROM_EMAIL,
  SMTP_FROM_NAME: envVars.SMTP_FROM_NAME,
  SMTP_SECURE: envVars.SMTP_SECURE,

  JWT_AT_SECRET: envVars.JWT_AT_SECRET,
  JWT_RT_SECRET: envVars.JWT_RT_SECRET,

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

  HSM_STORAGE_S3_ACCESS_KEY: envVars.HSM_STORAGE_S3_ACCESS_KEY,
  HSM_STORAGE_S3_FORCE_PATH_STYLE: envVars.HSM_STORAGE_S3_FORCE_PATH_STYLE,
  HSM_STORAGE_S3_HOST: envVars.HSM_STORAGE_S3_HOST,
  HSM_STORAGE_S3_REGION: envVars.HSM_STORAGE_S3_REGION,
  HSM_STORAGE_S3_SECRET_KEY: envVars.HSM_STORAGE_S3_SECRET_KEY,
} as const);
export type Envs = Readonly<typeof envs>;
