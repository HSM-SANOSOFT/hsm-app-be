import type { CodeOf } from '../helper/labelByCode';
import { labelOf } from '../helper/labelByCode';

const SEXO = {
  MASCULINO: { code: 'M', label: 'Masculino' },
  FEMENINO: { code: 'F', label: 'Femenino' },
  OTRO: { code: 'O', label: 'Otro' },
} as const;
export type Sexo = CodeOf<typeof SEXO>;
export const SexoLabel = labelOf(SEXO);

const GENERO = {
  MASCULINO: { code: 'M', label: 'Masculino' },
  FEMENINO: { code: 'F', label: 'Femenino' },
  OTRO: { code: 'O', label: 'Otro' },
} as const;
export type Genero = CodeOf<typeof GENERO>;
export const GeneroLabel = labelOf(GENERO);
