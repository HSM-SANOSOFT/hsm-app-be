import path from 'node:path';
import { fileURLToPath } from 'node:url';
import antfu from '@antfu/eslint-config';
import globals from 'globals';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export default antfu(
  {
    type: 'app',
    typescript: true,
    formatters: true,
    stylistic: {
      indent: 2,
      semi: true,
      quotes: 'single',
    },
  },
  {
    ignores: [
      '**/node_modules/**',
      '**/dist/**',
      '**/build/**',
      '**/.next/**',
      '**/coverage/**',
      '.github/workflows/**',
      'eslint.config.mjs',
      './pnpm-store/**',
    ],
    languageOptions: {
      globals: {
        ...globals.node,
        ...globals.jest,
      },
      parserOptions: {
        projectService: true,
        tsconfigRootDir: __dirname,
      },
    },

    rules: {
      'no-unused-vars': 'off',
      'ts/no-unused-vars': ['warn', { argsIgnorePattern: '^_' }],
      'ts/no-use-before-define': 'error',

      'ts/consistent-type-imports': 'error',
      'ts/triple-slash-reference': 'error',
      'ts/no-import-type-side-effects': 'error',
      'ts/no-require-imports': 'error',

      'ts/no-deprecated': 'error',
      'ts/no-empty-function': 'error',
      'ts/no-empty-interface': 'error',
      'ts/no-empty-object-type': 'error',

      'ts/no-unsafe-return': 'error',
      'ts/no-explicit-any': 'error',
      'ts/no-unsafe-call': 'error',
      'ts/no-unsafe-member-access': 'error',
      'ts/no-unsafe-assignment': 'error',
      'ts/no-floating-promises': 'error',

      'ts/no-unnecessary-boolean-literal-compare': 'error',
      'ts/no-unnecessary-type-arguments': 'error',
      'ts/no-unnecessary-type-assertion': 'error',
      'ts/no-unnecessary-type-constraint': 'error',
      'ts/no-unnecessary-template-expression': 'error',
      'ts/no-unnecessary-qualifier': 'error',
      'ts/no-unnecessary-parameter-property-assignment': 'error',

      'perfectionist/sort-imports': ['error', {
        type: 'natural',
        groups: [
          'side-effect',
          'builtin',
          'external',
          'internal',
          ['parent', 'sibling', 'index'],
          'type',
          'object',
          'unknown',
        ],
        tsconfigRootDir: __dirname,
      }],

      'no-console': 'warn',
      'no-debugger': 'error',
      'eol-last': ['error', 'always'],

      'unicorn/filename-case': ['error', {
        case: 'kebabCase',
        ignore: ['README.md'],
      }],
    },
  },
);
