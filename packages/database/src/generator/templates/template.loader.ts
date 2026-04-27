import { loadTemplate } from '@hsm-lib/database/generator/utils';

export const indexTemplate = loadTemplate<{
  files: string[];
}>('index.template.hbs');
