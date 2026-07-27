import { makeStateKey, type StateKey } from '@angular/core';
import * as joi from 'joi';

/**
 * Transfer-state key carrying the non-secret runtime config from the SSR server
 * to the browser (U10). The SSR server reads `process.env`, validates, and seeds
 * this key; the browser reads it back during hydration — no boot `config.json`
 * fetch.
 */
export const CONFIG_STATE_KEY: StateKey<AppConfig> =
  makeStateKey<AppConfig>('hsm.config');

/** Runtime app config, seeded from the SSR server's env via transfer state. */
export interface AppConfig {
  /** Host-only API base, e.g. `http://localhost:4201` (version per-endpoint). */
  apiBaseUrl: string;
}

const schema = joi
  .object<AppConfig>({
    apiBaseUrl: joi.string().uri({ allowRelative: true }).required(),
  })
  .required();

/** Validate a raw runtime-config payload; throws on a missing/invalid field. */
export function validateConfig(raw: unknown): AppConfig {
  const { error, value } = schema.validate(raw, {
    convert: true,
    allowUnknown: true,
  });
  if (error) {
    throw new Error(`Invalid runtime config: ${error.message}`);
  }
  return value as AppConfig;
}
