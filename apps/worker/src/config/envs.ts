import * as joi from 'joi';

interface EnvVars {
  HSM_DB_POSTGRES_HOST: string;
  HSM_DB_POSTGRES_PORT: number;
  HSM_DB_POSTGRES_USER: string;
  HSM_DB_POSTGRES_PASSWORD: string;
  HSM_DB_POSTGRES_DB: string;

  HSM_DB_REDIS_HOST: string;
  HSM_DB_REDIS_PORT: number;
  HSM_DB_REDIS_PASSWORD: string;
}

const envSchema = joi
  .object({
    HSM_DB_POSTGRES_HOST: joi.string().required(),
    HSM_DB_POSTGRES_PORT: joi.number().default(5432),
    HSM_DB_POSTGRES_USER: joi.string().required(),
    HSM_DB_POSTGRES_PASSWORD: joi.string().required(),
    HSM_DB_POSTGRES_DB: joi.string().required(),

    HSM_DB_REDIS_HOST: joi.string().required(),
    HSM_DB_REDIS_PORT: joi.number().default(6379),
    HSM_DB_REDIS_PASSWORD: joi.string().required(),
  })
  .unknown()
  .required();

const validation = envSchema.validate(process.env);

if (validation.error) {
  throw new Error(`Config validation error: ${validation.error.message}`);
}

const envVars: EnvVars = validation.value as EnvVars;

export const envs = {
  HSM_DB_POSTGRES_HOST: envVars.HSM_DB_POSTGRES_HOST,
  HSM_DB_POSTGRES_PORT: envVars.HSM_DB_POSTGRES_PORT,
  HSM_DB_POSTGRES_USER: envVars.HSM_DB_POSTGRES_USER,
  HSM_DB_POSTGRES_PASSWORD: envVars.HSM_DB_POSTGRES_PASSWORD,
  HSM_DB_POSTGRES_DB: envVars.HSM_DB_POSTGRES_DB,

  HSM_DB_REDIS_HOST: envVars.HSM_DB_REDIS_HOST,
  HSM_DB_REDIS_PORT: envVars.HSM_DB_REDIS_PORT,
  HSM_DB_REDIS_PASSWORD: envVars.HSM_DB_REDIS_PASSWORD,
};
